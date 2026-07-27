using FluentAssertions;
using R3Async;
using StackExchange.Redis;
using AsyncResult = R3Async.Result;

namespace SignalsDotnet.SignalsStore.Redis.Tests;

/// <summary>
/// Runs against a real Redis on localhost:63790:
/// <c>docker run -d --name signals-redis-test -p 63790:6379 redis:7</c>
/// </summary>
public sealed class RedisReplayLatestSubjectStoreTests : IAsyncLifetime
{
    const string ConnectionString = "localhost:63790";

    IConnectionMultiplexer _connection = null!;
    string _namespace = null!;

    public async Task InitializeAsync()
    {
        _connection = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        _namespace = "test-" + Guid.NewGuid().ToString("N");
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    RedisReplayLatestSubjectStore CreateStore() =>
        new(_connection, new RedisReplayLatestSubjectStoreOptions { Namespace = _namespace });

    static async Task WaitUntil(Func<bool> predicate, string because)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!predicate())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Timed out waiting: " + because);

            await Task.Delay(20);
        }
    }

    sealed class Recorder<T> : AsyncObserver<T>
    {
        readonly List<T> _values = new();
        readonly List<Exception> _errors = new();
        readonly object _gate = new();

        public IReadOnlyList<T> Values
        {
            get
            {
                lock (_gate)
                    return _values.ToArray();
            }
        }

        public IReadOnlyList<Exception> Errors
        {
            get
            {
                lock (_gate)
                    return _errors.ToArray();
            }
        }

        public AsyncResult? CompletedWith { get; private set; }

        protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken)
        {
            lock (_gate)
                _values.Add(value);

            return default;
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            lock (_gate)
                _errors.Add(error);

            return default;
        }

        protected override ValueTask OnCompletedAsyncCore(AsyncResult result)
        {
            CompletedWith = result;
            return default;
        }
    }

    [Fact]
    public async Task Subscribing_emits_nothing_when_the_value_was_never_written()
    {
        var subject = CreateStore().CreateSubject<string>("missing");

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, CancellationToken.None);

        recorder.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task Subscribing_replays_the_persisted_value()
    {
        var subject = CreateStore().CreateSubject<string>("greeting");
        await subject.OnNextAsync("hello", CancellationToken.None);

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, CancellationToken.None);

        recorder.Values.Should().Equal("hello");
    }

    [Fact]
    public async Task The_value_is_persisted_and_survives_a_new_store_instance()
    {
        await CreateStore().CreateSubject<string>("persisted")
                           .OnNextAsync("durable", CancellationToken.None);

        var subject = CreateStore().CreateSubject<string>("persisted");

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, CancellationToken.None);

        recorder.Values.Should().Equal("durable");
    }

    [Fact]
    public async Task A_write_is_pushed_to_a_subscriber_of_another_store_instance()
    {
        var writer = CreateStore().CreateSubject<string>("shared");
        var reader = CreateStore().CreateSubject<string>("shared");

        var recorder = new Recorder<string>();
        await using var _ = await reader.Values.SubscribeAsync(recorder, CancellationToken.None);

        await writer.OnNextAsync("from the other process", CancellationToken.None);

        await WaitUntil(() => recorder.Values.Count == 1, "the value published by the other store");
        recorder.Values.Should().Equal("from the other process");
    }

    [Fact]
    public async Task Successive_writes_converge_on_the_final_value_in_order()
    {
        // The subscriber side is a replay-latest channel of capacity 1 with drop-oldest
        // backpressure: only the newest notification matters, so a burst of writes arriving faster
        // than they are consumed is allowed to skip intermediate values. What must still hold is
        // that whatever does arrive is strictly increasing (never observes a version older than one
        // already delivered) and that the final write is eventually observed.
        var writer = CreateStore().CreateSubject<int>("counter");
        var reader = CreateStore().CreateSubject<int>("counter");

        var recorder = new Recorder<int>();
        await using var _ = await reader.Values.SubscribeAsync(recorder, CancellationToken.None);

        for (var i = 1; i <= 20; i++)
            await writer.OnNextAsync(i, CancellationToken.None);

        await WaitUntil(() => recorder.Values.Count > 0 && recorder.Values[^1] == 20,
                        $"the final value 20, saw [{string.Join(",", recorder.Values)}]");

        recorder.Values.Should().BeInAscendingOrder();
        recorder.Values.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task A_write_racing_the_subscription_is_not_lost()
    {
        // Subscribe and write concurrently: whichever way they interleave, the subscriber must end
        // up on the final value rather than stuck on the stale one. This is the case a naive
        // "read the key, then subscribe" implementation loses - subscribing before reading the key
        // in RedisReplayLatestSubjectStore.RedisValues.SubscribeAsyncCore is what guards against it.
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var id = "race" + attempt;
            var writer = CreateStore().CreateSubject<int>(id);
            var reader = CreateStore().CreateSubject<int>(id);

            await writer.OnNextAsync(1, CancellationToken.None);

            var recorder = new Recorder<int>();
            var write = Task.Run(async () => await writer.OnNextAsync(2, CancellationToken.None));
            var subscription = await reader.Values.SubscribeAsync(recorder, CancellationToken.None);

            await write;

            await WaitUntil(() => recorder.Values.Count > 0 && recorder.Values[^1] == 2,
                            $"attempt {attempt}: final value 2, saw [{string.Join(",", recorder.Values)}]");

            await subscription.DisposeAsync();
        }
    }

    [Fact]
    public async Task Disposing_the_subscription_stops_delivery()
    {
        var writer = CreateStore().CreateSubject<string>("stopped");
        var reader = CreateStore().CreateSubject<string>("stopped");

        var recorder = new Recorder<string>();
        var subscription = await reader.Values.SubscribeAsync(recorder, CancellationToken.None);

        await writer.OnNextAsync("first", CancellationToken.None);
        await WaitUntil(() => recorder.Values.Count == 1, "the first value");

        await subscription.DisposeAsync();

        await writer.OnNextAsync("second", CancellationToken.None);
        await Task.Delay(500);

        recorder.Values.Should().Equal("first");
    }

    [Fact]
    public async Task A_failure_during_initial_replay_unsubscribes_from_redis()
    {
        // AsyncObserver never rethrows from OnNextAsync/OnErrorResumeAsync/OnCompletedAsync - any
        // exception the observer implementation throws is routed to its own OnErrorResumeAsync (or
        // to UnhandledExceptionHandler) instead. So the only way the initial replay in
        // SubscribeAsyncCore genuinely throws is a Redis-level failure, not an observer failure:
        // here, a connection that is disposed out from under the read.
        var failingConnection = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        var store = new RedisReplayLatestSubjectStore(failingConnection, new RedisReplayLatestSubjectStoreOptions { Namespace = _namespace });
        var subject = store.CreateSubject<string>("throws-on-replay");
        await subject.OnNextAsync("value", CancellationToken.None);

        await failingConnection.CloseAsync();

        var recorder = new Recorder<string>();
        var act = async () => await subject.Values.SubscribeAsync(recorder, CancellationToken.None);
        await act.Should().ThrowAsync<Exception>();

        var channel = RedisChannel.Literal($"{_namespace}:throws-on-replay");
        await WaitUntil(() => _connection.GetSubscriber().SubscribedEndpoint(channel) is null,
                        "the channel to be unsubscribed");

        await failingConnection.DisposeAsync();
    }

    [Fact]
    public async Task A_connection_failure_completes_the_subscription_with_an_error()
    {
        // A dedicated connection, so severing it does not disturb the other tests sharing
        // _connection. ClientKillAsync on this connection's own physical socket, issued through the
        // shared connection, is what makes ConnectionFailed fire deterministically instead of
        // relying on a real network blip.
        var failingConnection = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        var store = new RedisReplayLatestSubjectStore(failingConnection, new RedisReplayLatestSubjectStoreOptions { Namespace = _namespace });
        var subject = store.CreateSubject<string>("connection-failure");

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, CancellationToken.None);

        var clientName = "test-killable-" + Guid.NewGuid().ToString("N");
        await failingConnection.GetDatabase().ExecuteAsync("CLIENT", "SETNAME", clientName);

        var killer = _connection.GetServer(_connection.GetEndPoints()[0]);
        var clientList = (string)(await killer.ExecuteAsync("CLIENT", "LIST"))!;
        var clientId = clientList
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.Contains($"name={clientName}"))
            .Split(' ')
            .Single(field => field.StartsWith("id=", StringComparison.Ordinal))["id=".Length..];

        await killer.ExecuteAsync("CLIENT", "KILL", "ID", clientId);

        await WaitUntil(() => recorder.CompletedWith is not null, "the connection failure to complete the subscription");
        recorder.CompletedWith!.Value.IsFailure.Should().BeTrue();

        await failingConnection.DisposeAsync();
    }

    [Fact]
    public async Task Distinct_ids_do_not_share_state()
    {
        var store = CreateStore();
        var a = store.CreateSubject<string>("a");
        var b = store.CreateSubject<string>("b");

        await a.OnNextAsync("value-a", CancellationToken.None);
        await b.OnNextAsync("value-b", CancellationToken.None);

        var recorderA = new Recorder<string>();
        var recorderB = new Recorder<string>();
        await using var _ = await a.Values.SubscribeAsync(recorderA, CancellationToken.None);
        await using var __ = await b.Values.SubscribeAsync(recorderB, CancellationToken.None);

        recorderA.Values.Should().Equal("value-a");
        recorderB.Values.Should().Equal("value-b");
    }

    [Fact]
    public async Task Completing_successfully_is_delivered_to_a_live_subscriber()
    {
        var writer = CreateStore().CreateSubject<string>("completes");
        var reader = CreateStore().CreateSubject<string>("completes");

        var recorder = new Recorder<string>();
        await using var _ = await reader.Values.SubscribeAsync(recorder, CancellationToken.None);

        await writer.OnCompletedAsync(AsyncResult.Success);

        await WaitUntil(() => recorder.CompletedWith is not null, "the completion to be observed");
        recorder.CompletedWith!.Value.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Completing_with_an_error_is_delivered_to_a_live_subscriber()
    {
        var writer = CreateStore().CreateSubject<string>("completes-with-error");
        var reader = CreateStore().CreateSubject<string>("completes-with-error");

        var recorder = new Recorder<string>();
        await using var _ = await reader.Values.SubscribeAsync(recorder, CancellationToken.None);

        await writer.OnCompletedAsync(AsyncResult.Failure(new InvalidOperationException("boom")));

        await WaitUntil(() => recorder.CompletedWith is not null, "the completion to be observed");
        var result = recorder.CompletedWith!.Value;
        result.IsFailure.Should().BeTrue();
        result.Exception!.Message.Should().Be("boom");
    }

    [Fact]
    public async Task Subscribing_after_completion_replays_completion_instead_of_hanging()
    {
        var subject = CreateStore().CreateSubject<string>("already-completed");
        await subject.OnNextAsync("last value", CancellationToken.None);
        await subject.OnCompletedAsync(AsyncResult.Success);

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, CancellationToken.None);

        // A fresh subscriber to an already-completed subject must be told so directly, rather than
        // replaying the last value and then waiting forever for a completion that already happened.
        recorder.CompletedWith.Should().NotBeNull();
        recorder.CompletedWith!.Value.IsSuccess.Should().BeTrue();
        recorder.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task A_value_written_after_completion_is_ignored()
    {
        var subject = CreateStore().CreateSubject<string>("no-writes-after-completion");
        await subject.OnNextAsync("before", CancellationToken.None);
        await subject.OnCompletedAsync(AsyncResult.Success);

        await subject.OnNextAsync("after", CancellationToken.None);

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, CancellationToken.None);

        recorder.CompletedWith.Should().NotBeNull();
        recorder.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task A_completion_written_after_completion_is_ignored()
    {
        var subject = CreateStore().CreateSubject<string>("no-double-completion");
        await subject.OnCompletedAsync(AsyncResult.Success);

        await subject.OnCompletedAsync(AsyncResult.Failure(new InvalidOperationException("should not apply")));

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, CancellationToken.None);

        recorder.CompletedWith!.Value.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_resumable_error_does_not_persist_and_is_not_replayed()
    {
        var writer = CreateStore().CreateSubject<string>("resumable-error");
        await writer.OnNextAsync("value", CancellationToken.None);

        var live = new Recorder<string>();
        await using (var _ = await CreateStore().CreateSubject<string>("resumable-error").Values.SubscribeAsync(live, CancellationToken.None))
        {
            await writer.OnErrorResumeAsync(new InvalidOperationException("transient"), CancellationToken.None);
            await WaitUntil(() => live.Errors.Count == 1, "the resumable error to be observed");
        }

        var afterward = new Recorder<string>();
        await using var __ = await CreateStore().CreateSubject<string>("resumable-error").Values.SubscribeAsync(afterward, CancellationToken.None);

        afterward.Values.Should().Equal("value");
        afterward.Errors.Should().BeEmpty();
        afterward.CompletedWith.Should().BeNull();
    }
}
