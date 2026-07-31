using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using R3;
using R3Async;
using SignalsDotnet.SignalsStore.SignalR.Client;
using SignalsDotnet.SignalsStore.SignalR.Tests.Helpers;
using AsyncResult = R3Async.Result;

namespace SignalsDotnet.SignalsStore.SignalR.Tests;

public sealed class SignalRSignalsStoreTests : IAsyncLifetime
{
    const int TestTimeoutMs = 3000;

    readonly SubjectStoreHubFixture _fixture = new();
    readonly CancellationTokenSource _timeoutCts = new(TimeSpan.FromMilliseconds(TestTimeoutMs));

    CancellationToken TimeoutToken => _timeoutCts.Token;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
        _timeoutCts.Dispose();
    }

    Task<ISubjectStore> ConnectStoreAsync()
    {
        ISubjectStore store = new SignalRSubjectStore(_ => new ValueTask<HubConnection>(_fixture.ConnectAsync()));
        return Task.FromResult(store);
    }

    (ISubjectStore Store, Func<HubConnection?> LatestConnection) ConnectStoreCapturingConnection()
    {
        HubConnection? latest = null;
        ISubjectStore store = new SignalRSubjectStore(async cancellationToken =>
        {
            var connection = await _fixture.ConnectAsync();
            latest = connection;
            return connection;
        });
        return (store, () => latest);
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

    static async Task WaitUntil(Func<bool> predicate, string because, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!predicate())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Timed out waiting: " + because);

            await Task.Yield();
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task A_value_published_by_one_client_is_delivered_to_a_subscriber_on_another_client()
    {
        var writerStore = await ConnectStoreAsync();
        var readerStore = await ConnectStoreAsync();

        var writer = writerStore.CreateSubject<string>("greeting");
        var reader = readerStore.CreateSubject<string>("greeting");

        var recorder = new Recorder<string>();
        await using var _ = await reader.Values.SubscribeAsync(recorder, TimeoutToken);

        await writer.OnNextAsync("hello", TimeoutToken);

        await WaitUntil(() => recorder.Values.Count == 1, "the value published by the other client", TimeoutToken);
        recorder.Values.Should().Equal("hello");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Two_subscribers_on_the_same_id_both_receive_the_same_value()
    {
        var store = await ConnectStoreAsync();
        var writer = store.CreateSubject<string>("fanout");

        var recorderA = new Recorder<string>();
        var recorderB = new Recorder<string>();
        await using var _ = await store.CreateSubject<string>("fanout").Values.SubscribeAsync(recorderA, TimeoutToken);
        await using var __ = await store.CreateSubject<string>("fanout").Values.SubscribeAsync(recorderB, TimeoutToken);

        await writer.OnNextAsync("broadcast", TimeoutToken);

        await WaitUntil(() => recorderA.Values.Count == 1 && recorderB.Values.Count == 1, "both subscribers to observe the value", TimeoutToken);
        recorderA.Values.Should().Equal("broadcast");
        recorderB.Values.Should().Equal("broadcast");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Distinct_ids_do_not_share_state()
    {
        var store = await ConnectStoreAsync();
        var a = store.CreateSubject<string>("id-a");
        var b = store.CreateSubject<string>("id-b");

        var recorderA = new Recorder<string>();
        var recorderB = new Recorder<string>();
        await using var _ = await a.Values.SubscribeAsync(recorderA, TimeoutToken);
        await using var __ = await b.Values.SubscribeAsync(recorderB, TimeoutToken);

        await a.OnNextAsync("value-a", TimeoutToken);
        await b.OnNextAsync("value-b", TimeoutToken);

        await WaitUntil(() => recorderA.Values.Count == 1 && recorderB.Values.Count == 1, "both ids to be observed independently", TimeoutToken);
        recorderA.Values.Should().Equal("value-a");
        recorderB.Values.Should().Equal("value-b");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task An_error_resume_is_delivered_without_ending_the_subscription()
    {
        var store = await ConnectStoreAsync();
        var subject = store.CreateSubject<string>("resumable-error");

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, TimeoutToken);

        await subject.OnErrorResumeAsync(new InvalidOperationException("transient"), TimeoutToken);
        await WaitUntil(() => recorder.Errors.Count == 1, "the resumable error to be observed", TimeoutToken);

        await subject.OnNextAsync("still alive", TimeoutToken);
        await WaitUntil(() => recorder.Values.Count == 1, "a value published after the resumable error", TimeoutToken);

        recorder.CompletedWith.Should().BeNull();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Completing_successfully_is_delivered_to_a_live_subscriber()
    {
        var store = await ConnectStoreAsync();
        var subject = store.CreateSubject<string>("completes");

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, TimeoutToken);

        await subject.OnCompletedAsync(AsyncResult.Success);

        await WaitUntil(() => recorder.CompletedWith is not null, "the completion to be observed", TimeoutToken);
        recorder.CompletedWith!.Value.IsSuccess.Should().BeTrue();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Completing_with_an_error_surfaces_the_message_through_a_SignalRSubjectException()
    {
        var store = await ConnectStoreAsync();
        var subject = store.CreateSubject<string>("completes-with-error");

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, TimeoutToken);

        await subject.OnCompletedAsync(AsyncResult.Failure(new InvalidOperationException("boom")));

        await WaitUntil(() => recorder.CompletedWith is not null, "the completion to be observed", TimeoutToken);
        var result = recorder.CompletedWith!.Value;
        result.IsFailure.Should().BeTrue();
        result.Exception.Should().BeOfType<SignalRSubjectException>();
        result.Exception!.Message.Should().Be("boom");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Disposing_the_subscription_stops_delivery()
    {
        var store = await ConnectStoreAsync();
        var subject = store.CreateSubject<string>("stopped");

        var recorder = new Recorder<string>();
        var subscription = await subject.Values.SubscribeAsync(recorder, TimeoutToken);

        await subject.OnNextAsync("first", TimeoutToken);
        await WaitUntil(() => recorder.Values.Count == 1, "the first value", TimeoutToken);

        await subscription.DisposeAsync();

        var witness = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(witness, TimeoutToken);

        await subject.OnNextAsync("second", TimeoutToken);
        await WaitUntil(() => witness.Values.Contains("second"), "the witness subscription to observe the second value", TimeoutToken);

        recorder.Values.Should().Equal("first");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Connection_dying_mid_stream_completes_the_observer_instead_of_hanging_forever()
    {
        var (store, latestConnection) = ConnectStoreCapturingConnection();
        var subject = store.CreateSubject<string>("dies-mid-stream");

        var recorder = new Recorder<string>();
        await using var _ = await subject.Values.SubscribeAsync(recorder, TimeoutToken);

        await subject.OnNextAsync("first", TimeoutToken);
        await WaitUntil(() => recorder.Values.Count == 1, "the first value", TimeoutToken);

        await latestConnection()!.StopAsync(TimeoutToken);

        await WaitUntil(() => recorder.CompletedWith is not null,
            "the observer to be completed once the underlying connection dies",
            TimeoutToken);

        recorder.CompletedWith!.Value.IsFailure.Should().BeTrue();
    }

    SignalRSignalStore ConnectSignalStore() =>
        SignalRSignalStore.Create(_fixture.HubUrl,
            builder => builder.WithUrl(_fixture.HubUrl, options => options.HttpMessageHandlerFactory = _ => _fixture.Server.CreateHandler()));

    [Fact(Timeout = TestTimeoutMs)]
    public async Task End_to_end_through_SignalRSignalStore_reflects_writes_from_another_client()
    {
        var writerStore = await ConnectStoreAsync();
        var readerSignalStore = ConnectSignalStore();

        var writer = writerStore.CreateSubject<int>("counter");
        var readerProxy = readerSignalStore.CreateSignalProxy("counter", 0);

        using var _ = readerProxy.Values.Subscribe(x => { });
        await readerProxy.EnsureConnectedAsync(TimeoutToken);

        await writer.OnNextAsync(42, TimeoutToken);

        await WaitUntil(() => readerProxy.Value == 42, $"reader to observe 42, saw {readerProxy.Value}", TimeoutToken);
    }
}
