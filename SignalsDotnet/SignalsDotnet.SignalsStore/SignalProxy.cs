using R3;
using R3Async;
using R3Async.R3Interop;
using R3Async.Subjects;
using SignalsDotnet.Configuration;
using AsyncResult = R3Async.Result;
using State = SignalsDotnet.SignalsStore.ConnectionState;
using Unit = R3Async.Unit;

namespace SignalsDotnet.SignalsStore;

internal sealed class SignalProxy<T> : ISignalProxy<T>
{
    readonly IReadOnlySignal<T> _valueSignal;
    readonly Signal<State> _connectionState;
    readonly Signal<bool> _hasValueObserver;
    readonly Subject<T> _localWrites = new();
    readonly Action<T>? _onValueSet;
    readonly R3Async.Subjects.ISubject<Unit> _reconnectionRequested = Subject.CreateBehavior(Unit.Default);
    readonly Signal<bool> _hasExternalSubscriber = Signal.Create(false);

    T _latestWrite;
    int _externalSubscriberCount;

    public SignalProxy(string id,
                       T startValue,
                       AsyncObservable<T> upstream,
                       Action<T>? onValueSet = null,
                       ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        Id = id;
        _onValueSet = onValueSet;
        _latestWrite = startValue;
        _connectionState = Signal.Create<State>(new State.Disconnected());
        _hasValueObserver = Signal.Create(false);

        var trackedUpstream = new ConnectionScope(this, upstream)
                              .Share(ShareConfig.ResetOnCompletionAndRefCountZero);

        var syncUpstream = _reconnectionRequested
            .Values
            .Select(_ => trackedUpstream
                .RouteSubscriptionErrorToCompletionError()
                .CatchAndIgnoreErrorResume(_ => AsyncObservable.Empty<T>()))
            .Switch()
            .ToObservable(new ToObservableConfiguration
            {
                SubscribeStrategy = AsyncToSyncStrategy.FireAndForget(OnConnectionFaulted),
                DisposeStrategy = AsyncToSyncStrategy.FireAndForget(OnConnectionFaulted)
            });

        var values = syncUpstream.Merge(_localWrites)
                                 .Prepend(() => _latestWrite);

        _valueSignal = Signal.FromObservable(values,
                                             config => (configuration?.Invoke(config) ?? config) with
                                             {
                                                 SubscriptionStrategy = SubscriptionStrategy.RefCount
                                             });

        void OnConnectionFaulted(Exception error) =>
            Publish(_connectionState, new State.Disconnected(error));
    }

    public string Id { get; }

    public IReadOnlySignal<ConnectionState> ConnectionState => _connectionState;
    public IReadOnlySignal<bool> HasValueObserver => _hasValueObserver;

    public async ValueTask EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connectionState.UntrackedValue is State.Connected)
            return;

        if (!_hasExternalSubscriber.UntrackedValue)
            throw NoObserversException();

        if (_connectionState.UntrackedValue is State.Disconnected)
        {
            await _reconnectionRequested.OnNextAsync(Unit.Default, cancellationToken);
        }
        else
        {
            await _connectionState.Values
                                  .Where(static x => x is not State.Connecting)
                                  .FirstAsync(cancellationToken);
        }

        if (_connectionState.UntrackedValue is State.Disconnected { Error: { } error })
            throw error;

        if (_connectionState.UntrackedValue is not State.Connected)
            throw ConnectFailedException();
    }

    static InvalidOperationException NoObserversException() =>
        new("Cannot ensure the signal proxy is connected while nobody is observing its Values.");

    static InvalidOperationException ConnectFailedException() =>
        new("Failed to connect the signal proxy.");

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
        Publish(_hasValueObserver, true);
        if (_connectionState.UntrackedValue is not State.Connected)
            Publish(_connectionState, new State.Connecting());
    }

    void OnConnected() => Publish(_connectionState, new State.Connected());

    void OnConnectFailed(Exception error)
    {
        Publish(_connectionState, new State.Disconnected(error));
        Publish(_hasValueObserver, false);
    }

    void OnUpstreamCompleted(AsyncResult result)
    {
        if (result.IsFailure)
            Publish(_connectionState, new State.Disconnected(result.Exception));
    }

    void OnDisconnected()
    {
        Publish(_hasValueObserver, false);

        // OnUpstreamCompleted may already have recorded a Disconnected(error): a completion can
        // synchronously trigger this teardown (e.g. via Share's ResetOnRefCountZero), so that
        // error must not be clobbered by a plain Disconnected here.
        if (_connectionState.UntrackedValue is not State.Disconnected)
            Publish(_connectionState, new State.Disconnected());
    }

    public T Value
    {
        get => _valueSignal.Value;
        set
        {
            _latestWrite = value;
            _localWrites.OnNext(value);
            _onValueSet?.Invoke(value);
        }
    }

    public T UntrackedValue => _valueSignal.UntrackedValue;
    public Observable<T> Values => new SubscriberCountTracker<T>(this, _valueSignal.Values);
    public Observable<T> FutureValues => new SubscriberCountTracker<T>(this, _valueSignal.FutureValues);

    Observable<R3.Unit> IReadOnlySignal.Values => ((IReadOnlySignal)_valueSignal).Values;
    Observable<R3.Unit> INotifySignalChanged.FutureValues => ((INotifySignalChanged)_valueSignal).FutureValues;
    object? IReadOnlySignal.Value => Value;
    object? IReadOnlySignal.UntrackedValue => UntrackedValue;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged
    {
        // Must participate in the same external-subscriber bookkeeping as Values/FutureValues:
        // otherwise a caller observing only via INotifyPropertyChanged (e.g. WPF bindings) never
        // sets _hasExternalSubscriber, and EnsureConnectedAsync wrongly throws NoObserversException.
        add
        {
            if (Interlocked.Increment(ref _externalSubscriberCount) == 1)
                Publish(_hasExternalSubscriber, true);

            _valueSignal.PropertyChanged += value;
        }
        remove
        {
            _valueSignal.PropertyChanged -= value;

            if (Interlocked.Decrement(ref _externalSubscriberCount) == 0)
                Publish(_hasExternalSubscriber, false);
        }
    }


    /// <summary>
    /// Tracks how many external subscribers are attached to Values/FutureValues, independent of
    /// HasValueObserver (which reflects the upstream connection, not whether anyone is asking for
    /// one). EnsureConnectedAsync uses this instead, since a reconnect must be possible even while
    /// HasValueObserver is momentarily false following a dropped connection.
    /// </summary>
    sealed class SubscriberCountTracker<TValue>(SignalProxy<T> proxy, Observable<TValue> inner) : Observable<TValue>
    {
        protected override IDisposable SubscribeCore(Observer<TValue> observer)
        {
            if (Interlocked.Increment(ref proxy._externalSubscriberCount) == 1)
                Publish(proxy._hasExternalSubscriber, true);

            var subscription = inner.Subscribe(observer.OnNext, observer.OnErrorResume, observer.OnCompleted);
            return new Unsubscriber(proxy, subscription);
        }

        sealed class Unsubscriber(SignalProxy<T> proxy, IDisposable inner) : IDisposable
        {
            int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                inner.Dispose();
                if (Interlocked.Decrement(ref proxy._externalSubscriberCount) == 0)
                    Publish(proxy._hasExternalSubscriber, false);
            }
        }
    }

    sealed class ConnectionScope(SignalProxy<T> proxy, AsyncObservable<T> upstream) : AsyncObservable<T>
    {
        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer,
                                                                                CancellationToken cancellationToken)
        {
            proxy.OnConnecting();

            IAsyncDisposable subscription;
            try
            {
                subscription = await upstream.SubscribeAsync(new ForwardingObserver(proxy, observer), cancellationToken);
            }
            catch (Exception error)
            {
                proxy.OnConnectFailed(error);
                throw;
            }

            proxy.OnConnected();
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