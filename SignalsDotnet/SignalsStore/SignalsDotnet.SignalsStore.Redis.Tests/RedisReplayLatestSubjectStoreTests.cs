using FluentAssertions;
using R3Async;
using StackExchange.Redis;
using AsyncResult = R3Async.Result;

namespace SignalsDotnet.SignalsStore.Redis.Tests;

/// <summary>
/// Runs against a Redis instance started via Testcontainers, shared across this test class.
/// </summary>
[Collection(RedisContainerCollection.Name)]
public sealed class RedisReplayLatestSubjectStoreTests : IAsyncLifetime
{
    const int TestTimeoutMs = 30000;

    readonly RedisContainerFixture _fixture;
    readonly CancellationTokenSource _timeoutCts = new(TimeSpan.FromMilliseconds(TestTimeoutMs));

    IConnectionMultiplexer _connection = null!;
    string _namespace = null!;

    CancellationToken TimeoutToken => _timeoutCts.Token;

    public RedisReplayLatestSubjectStoreTests(RedisContainerFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _connection = _fixture.Connection;
        _namespace = "test-" + Guid.NewGuid().ToString("N");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _timeoutCts.Dispose();
        return Task.CompletedTask;
    }

    RedisReplayLatestSubjectStore CreateStore() =>
        new(async _ => await ConnectionMultiplexer.ConnectAsync(_fixture.ConnectionString),
            new RedisReplayLatestSubjectStoreOptions { Namespace = _namespace });

    async Task WaitUntil(Func<bool> predicate, string because)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!predicate())
        {
            TimeoutToken.ThrowIfCancellationRequested();

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Timed out waiting: " + because);

            await Task.Yield();
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

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Subscribing_emits_nothing_when_the_value_was_never_written()
    {
        var subject = CreateStore().CreateSubject<string>("missing");

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, TimeoutToken);

        recorder.Values.Should().BeEmpty();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Subscribing_replays_the_persisted_value()
    {
        var subject = CreateStore().CreateSubject<string>("greeting");
        await subject.OnNextAsync("hello", TimeoutToken);

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, TimeoutToken);

        recorder.Values.Should().Equal("hello");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task The_value_is_persisted_and_survives_a_new_store_instance()
    {
        await CreateStore().CreateSubject<string>("persisted")
                           .OnNextAsync("durable", TimeoutToken);

        var subject = CreateStore().CreateSubject<string>("persisted");

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, TimeoutToken);

        recorder.Values.Should().Equal("durable");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task A_write_is_pushed_to_a_subscriber_of_another_store_instance()
    {
        var writer = CreateStore().CreateSubject<string>("shared");
        var reader = CreateStore().CreateSubject<string>("shared");

        var recorder = new Recorder<string>();
        await using var _ = await reader.Values.SubscribeAsync(recorder, TimeoutToken);

        await writer.OnNextAsync("from the other process", TimeoutToken);

        await WaitUntil(() => recorder.Values.Count == 1, "the value published by the other store");
        recorder.Values.Should().Equal("from the other process");
    }

    [Fact(Timeout = TestTimeoutMs)]
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
        await using var _ = await reader.Values.SubscribeAsync(recorder, TimeoutToken);

        for (var i = 1; i <= 20; i++)
            await writer.OnNextAsync(i, TimeoutToken);

        await WaitUntil(() => recorder.Values.Count > 0 && recorder.Values[^1] == 20,
                        $"the final value 20, saw [{string.Join(",", recorder.Values)}]");

        recorder.Values.Should().BeInAscendingOrder();
        recorder.Values.Should().OnlyHaveUniqueItems();
    }

    [Fact(Timeout = TestTimeoutMs)]
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

            await writer.OnNextAsync(1, TimeoutToken);

            var recorder = new Recorder<int>();
            var write = Task.Run(async () => await writer.OnNextAsync(2, TimeoutToken));
            var subscription = await reader.Values.SubscribeAsync(recorder, TimeoutToken);

            await write;

            await WaitUntil(() => recorder.Values.Count > 0 && recorder.Values[^1] == 2,
                            $"attempt {attempt}: final value 2, saw [{string.Join(",", recorder.Values)}]");

            await subscription.DisposeAsync();
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Disposing_the_subscription_stops_delivery()
    {
        var writer = CreateStore().CreateSubject<string>("stopped");
        var reader = CreateStore().CreateSubject<string>("stopped");

        var recorder = new Recorder<string>();
        var subscription = await reader.Values.SubscribeAsync(recorder, TimeoutToken);

        await writer.OnNextAsync("first", TimeoutToken);
        await WaitUntil(() => recorder.Values.Count == 1, "the first value");

        await subscription.DisposeAsync();

        var witness = new Recorder<string>();
        await using var _ = await reader.Values.SubscribeAsync(witness, TimeoutToken);

        await writer.OnNextAsync("second", TimeoutToken);
        await WaitUntil(() => witness.Values.Contains("second"), "the witness subscription to observe the second value");

        recorder.Values.Should().Equal("first");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task A_connection_failure_completes_the_subscription_with_an_error()
    {
        // A dedicated connection, so severing it does not disturb the other tests. ClientKillAsync
        // on this connection's own physical socket, issued through the fixture's own connection, is
        // what makes ConnectionFailed fire deterministically instead of relying on a real network
        // blip. The live subscription below holds the store's connection reference open for the
        // rest of the test, so failingConnection stays the same physical connection throughout.
        IConnectionMultiplexer? failingConnection = null;
        var store = new RedisReplayLatestSubjectStore(async _ =>
        {
            failingConnection = await ConnectionMultiplexer.ConnectAsync(_fixture.ConnectionString);
            return failingConnection;
        }, new RedisReplayLatestSubjectStoreOptions { Namespace = _namespace });
        var subject = store.CreateSubject<string>("connection-failure");

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, TimeoutToken);

        var clientName = "test-killable-" + Guid.NewGuid().ToString("N");
        await failingConnection!.GetDatabase().ExecuteAsync("CLIENT", "SETNAME", clientName);

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

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Distinct_ids_do_not_share_state()
    {
        var store = CreateStore();
        var a = store.CreateSubject<string>("a");
        var b = store.CreateSubject<string>("b");

        await a.OnNextAsync("value-a", TimeoutToken);
        await b.OnNextAsync("value-b", TimeoutToken);

        var recorderA = new Recorder<string>();
        var recorderB = new Recorder<string>();
        await using var _ = await a.Values.SubscribeAsync(recorderA, TimeoutToken);
        await using var __ = await b.Values.SubscribeAsync(recorderB, TimeoutToken);

        recorderA.Values.Should().Equal("value-a");
        recorderB.Values.Should().Equal("value-b");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Completing_successfully_is_delivered_to_a_live_subscriber()
    {
        var writer = CreateStore().CreateSubject<string>("completes");
        var reader = CreateStore().CreateSubject<string>("completes");

        var recorder = new Recorder<string>();
        await using var _ = await reader.Values.SubscribeAsync(recorder, TimeoutToken);

        await writer.OnCompletedAsync(AsyncResult.Success);

        await WaitUntil(() => recorder.CompletedWith is not null, "the completion to be observed");
        recorder.CompletedWith!.Value.IsSuccess.Should().BeTrue();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Completing_with_an_error_is_delivered_to_a_live_subscriber()
    {
        var writer = CreateStore().CreateSubject<string>("completes-with-error");
        var reader = CreateStore().CreateSubject<string>("completes-with-error");

        var recorder = new Recorder<string>();
        await using var _ = await reader.Values.SubscribeAsync(recorder, TimeoutToken);

        await writer.OnCompletedAsync(AsyncResult.Failure(new InvalidOperationException("boom")));

        await WaitUntil(() => recorder.CompletedWith is not null, "the completion to be observed");
        var result = recorder.CompletedWith!.Value;
        result.IsFailure.Should().BeTrue();
        result.Exception!.Message.Should().Be("boom");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Subscribing_after_completion_replays_completion_instead_of_hanging()
    {
        var subject = CreateStore().CreateSubject<string>("already-completed");
        await subject.OnNextAsync("last value", TimeoutToken);
        await subject.OnCompletedAsync(AsyncResult.Success);

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, TimeoutToken);

        // A fresh subscriber to an already-completed subject must be told so directly, rather than
        // replaying the last value and then waiting forever for a completion that already happened.
        recorder.CompletedWith.Should().NotBeNull();
        recorder.CompletedWith!.Value.IsSuccess.Should().BeTrue();
        recorder.Values.Should().BeEmpty();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task A_value_written_after_completion_is_ignored()
    {
        var subject = CreateStore().CreateSubject<string>("no-writes-after-completion");
        await subject.OnNextAsync("before", TimeoutToken);
        await subject.OnCompletedAsync(AsyncResult.Success);

        await subject.OnNextAsync("after", TimeoutToken);

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, TimeoutToken);

        recorder.CompletedWith.Should().NotBeNull();
        recorder.Values.Should().BeEmpty();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task A_completion_written_after_completion_is_ignored()
    {
        var subject = CreateStore().CreateSubject<string>("no-double-completion");
        await subject.OnCompletedAsync(AsyncResult.Success);

        await subject.OnCompletedAsync(AsyncResult.Failure(new InvalidOperationException("should not apply")));

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, TimeoutToken);

        recorder.CompletedWith!.Value.IsSuccess.Should().BeTrue();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task A_resumable_error_does_not_persist_and_is_not_replayed()
    {
        var writer = CreateStore().CreateSubject<string>("resumable-error");
        await writer.OnNextAsync("value", TimeoutToken);

        var live = new Recorder<string>();
        await using (var _ = await CreateStore().CreateSubject<string>("resumable-error").Values.SubscribeAsync(live, TimeoutToken))
        {
            await writer.OnErrorResumeAsync(new InvalidOperationException("transient"), TimeoutToken);
            await WaitUntil(() => live.Errors.Count == 1, "the resumable error to be observed");
        }

        var afterward = new Recorder<string>();
        await using var __ = await CreateStore().CreateSubject<string>("resumable-error").Values.SubscribeAsync(afterward, TimeoutToken);

        afterward.Values.Should().Equal("value");
        afterward.Errors.Should().BeEmpty();
        afterward.CompletedWith.Should().BeNull();
    }
}
