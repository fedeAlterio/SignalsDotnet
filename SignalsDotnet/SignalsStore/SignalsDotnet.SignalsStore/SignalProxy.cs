using System.Runtime.ExceptionServices;
using R3;
using R3Async;
using R3Async.R3Interop;
using R3Async.Subjects;
using SignalsDotnet.Configuration;
using SignalsDotnet.Internals;
using AsyncResult = R3Async.Result;
using State = SignalsDotnet.SignalsStore.ConnectionState;
using Unit = R3Async.Unit;

namespace SignalsDotnet.SignalsStore;

internal sealed class SignalProxy<T> : FromObservableSignalRefCounted<T>, ISignalProxy<T>
{
    readonly R3Async.Subjects.ISubject<T> _subject;
    readonly Signal<State> _connectionState;
    TaskCompletionSource<bool> _lastConnectionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly R3Async.Subjects.ISubject<Unit> _reconnectionRequested = R3Async.Subjects.Subject.CreateBehavior(Unit.Default);
    readonly Signal<bool> _hasValueObserver = Signal.Create(false);
    readonly object _connectRequestLocker = new();

    public SignalProxy(string id,
                       T startValue,
                       R3Async.Subjects.ISubject<T> subject,
                       ReadonlySignalConfigurationDelegate<T?>? configuration = null)
        : base(startValue, ResolveConfiguration(configuration))
    {
        Id = id;
        _connectionState = Signal.Create<State>(new State.Disconnected());
        var obs = subject.Values.Share(ShareConfig.ResetOnCompletionAndRefCountZero);
        var trackedUpstream = new ConnectionScope(this, obs)
                              .Share(ShareConfig.ResetOnCompletionAndRefCountZero);

        _subject = subject.MapValues(_ => obs);

        var syncUpstream = _reconnectionRequested
            .Values
            .Select(_ => trackedUpstream
                .RouteSubscriptionErrorToCompletionError()
                .CatchAndIgnoreErrorResume(_ => AsyncObservable.Empty<T>()))
            .Switch()
            .ToObservable(new ToObservableConfiguration
            {
                SubscribeStrategy = AsyncToSyncStrategy.FireAndForget(),
                DisposeStrategy = AsyncToSyncStrategy.FireAndForget()
            });

        Observable = syncUpstream;
    }

    static ReadonlySignalConfiguration<T?> ResolveConfiguration(ReadonlySignalConfigurationDelegate<T?>? configuration)
    {
        var config = ReadonlySignalConfiguration<T?>.Default;
        return (configuration?.Invoke(config) ?? config) with
        {
            SubscriptionStrategy = SubscriptionStrategy.RefCount
        };
    }

    public string Id { get; }

    public R3Async.Subjects.ISubject<T> Subject => _subject;

    public IReadOnlySignal<ConnectionState> ConnectionState => _connectionState;
    public IReadOnlySignal<bool> HasValueObserver => _hasValueObserver;

    public async ValueTask EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_connectionState.UntrackedValue is State.Connected)
            return;

        if (!_hasValueObserver.UntrackedValue)
            throw NoObserversException();

        Task task;
        bool shouldNotifyRequest;
        lock (_connectRequestLocker)
        {
            var lastTcs = _lastConnectionTcs;
            if (!lastTcs.Task.IsCompleted)
            {
                task = lastTcs.Task;
                shouldNotifyRequest = false;
            }
            else if (_connectionState.UntrackedValue is State.Disconnected)
            {
                var newTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                task = newTcs.Task;
                _lastConnectionTcs = newTcs;
                shouldNotifyRequest = true;
            }
            else
            {
                return;
            }
        }

        if (shouldNotifyRequest)
            await _reconnectionRequested.OnNextAsync(Unit.Default, cancellationToken);

        await await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));

        if (_connectionState.UntrackedValue is State.Disconnected { Error: { } error })
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    static InvalidOperationException NoObserversException() =>
        new("Cannot ensure the signal proxy is connected while nobody is observing its Values.");

    static void Publish<TValue>(Signal<TValue> signal, TValue value)
    {
        try
        {
            signal.Value = value;
        }
        catch
        {
            // Ignored
        }
    }

    void OnConnecting()
    {
        Publish(_connectionState, new State.Connecting());
    }

    void OnConnected()
    {
        Publish(_connectionState, new State.Connected());
    }

    void OnConnectFailed(Exception error)
    {
        if (_connectionState.UntrackedValue is not State.Disconnected)
        {
            Publish(_connectionState, new State.Disconnected(error));
        }
    }

    void OnUpstreamCompleted(AsyncResult result)
    {
        Publish(_connectionState, new State.Disconnected(result.Exception));
    }

    void OnDisconnected()
    {
        if (_connectionState.UntrackedValue is not State.Disconnected)
        {
            Publish(_connectionState, new State.Disconnected());
        }
    }

    protected override void OnObservedChanged(bool isObserved) => Publish(_hasValueObserver, isObserved);

    sealed class ConnectionScope(SignalProxy<T> proxy, AsyncObservable<T> upstream) : AsyncObservable<T>
    {
        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer,
            CancellationToken cancellationToken)
        {
            var connectionCompletedTcs = proxy._lastConnectionTcs;
            IAsyncDisposable subscription;

            try
            {
                proxy.OnConnecting();
                subscription = await upstream.SubscribeAsync(new ForwardingObserver(proxy, observer), cancellationToken);
            }
            catch (Exception error)
            {
                proxy.OnConnectFailed(error);
                connectionCompletedTcs.TrySetException(error);
                throw;
            }

            try
            {
                proxy.OnConnected();
            }
            finally
            {
                connectionCompletedTcs.TrySetResult(true);
            }
            return new Disconnector(proxy, subscription);
        }

        sealed class ForwardingObserver(SignalProxy<T> proxy, AsyncObserver<T> observer) : AsyncObserver<T>
        {
            protected override ValueTask OnNextAsyncCore(T value, CancellationToken cancellationToken) =>
                observer.OnNextAsync(value, cancellationToken);

            protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken) =>
                observer.OnErrorResumeAsync(error, cancellationToken);

            protected override ValueTask OnCompletedAsyncCore(AsyncResult result)
            {
                proxy.OnUpstreamCompleted(result);
                return observer.OnCompletedAsync(result);
            }
        }

        sealed class Disconnector(SignalProxy<T> proxy, IAsyncDisposable subscription) : IAsyncDisposable
        {
            int _disposed;

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                try
                {
                    await subscription.DisposeAsync();
                }
                finally
                {
                    proxy.OnDisconnected();
                }
            }
        }
    }
}

file static class Ex
{
    public static AsyncObservable<T> RouteSubscriptionErrorToCompletionError<T>(this AsyncObservable<T> @this) =>
        new RouteSubscriptionErrorToCompletionErrorObservable<T>(@this);
}

sealed class RouteSubscriptionErrorToCompletionErrorObservable<T>(AsyncObservable<T> upstream) : AsyncObservable<T>
{
    protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer,
                                                                             CancellationToken cancellationToken)
    {
        try
        {
            return await upstream.SubscribeAsync(observer.Wrap(), cancellationToken);
        }
        catch (Exception error)
        {
            await observer.OnCompletedAsync(AsyncResult.Failure(error));
            return AsyncDisposable.Empty;
        }
    }
}