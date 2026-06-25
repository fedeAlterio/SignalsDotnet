using R3;
using SignalsDotnet.Helpers;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet.Internals;

internal sealed class ComputedObservable<T> : Observable<T>
{
    readonly Func<CancellationToken, ValueTask<T>> _func;
    readonly Func<Optional<T>> _fallbackValue;
    readonly Func<Unit, Observable<Unit>>? _scheduler;
    readonly ConcurrentChangeStrategy _concurrentChangeStrategy;

    public ComputedObservable(Func<CancellationToken, ValueTask<T>> func,
                              Func<Optional<T>> fallbackValue,
                              Func<Unit, Observable<Unit>>? scheduler = null,
                              ConcurrentChangeStrategy concurrentChangeStrategy = default)
    {
        _func = func;
        _fallbackValue = fallbackValue;
        _scheduler = scheduler;
        _concurrentChangeStrategy = concurrentChangeStrategy;
    }

    protected override IDisposable SubscribeCore(Observer<T> observer) => new Subscription(this, observer);

    sealed class Subscription : IDisposable
    {
        readonly ComputedObservable<T> _observable;
        readonly Observer<T> _observer;
        readonly CancellationTokenSource _disposed = new();
        DisposableBag _disconnectSubscription;

        readonly HashSet<INotifySignalChanged> _signalsRequested = new(ReferenceEqualityComparer<INotifySignalChanged>.Instance);
        readonly SyncCompletionSource _signalChangedAwaitable = new();
        DisposableBag _signalChangeSubscription;
        int _anySignalArrived;
        CancellationTokenSource? _cts;
        readonly Action<INotifySignalChanged> _onSignalRequested;
        readonly Action<Unit> _onSignalChanged;
        readonly Action<Unit> _setCompleted;

        public Subscription(ComputedObservable<T> observable, Observer<T> observer)
        {
            _observable = observable;
            _observer = observer;
            _onSignalRequested = OnSignalRequested;
            _onSignalChanged = OnSignalChanged;
            _setCompleted = _signalChangedAwaitable.SetCompleted;
            WatchSignalsChanges();
        }

        void OnSignalRequested(INotifySignalChanged signal)
        {
            if (!_signalsRequested.Add(signal)) return;
            signal.FutureValues.Subscribe(_onSignalChanged).AddTo(ref _signalChangeSubscription);
        }

        void OnSignalChanged(Unit _)
        {
            if (Interlocked.CompareExchange(ref _anySignalArrived, 1, 0) == 1) return;

            _signalChangeSubscription.Dispose();
            _cts?.Cancel();
            var scheduler = _observable._scheduler;
            if (scheduler is not null)
            {
                scheduler(Unit.Default).Subscribe(_setCompleted)
                                       .AddTo(ref _disconnectSubscription);
            }
            else
            {
                _signalChangedAwaitable.SetCompleted(Unit.Default);
            }
        }

        async void WatchSignalsChanges()
        {
            try
            {
                try
                {
                    var token = _disposed.Token;
                    using var _ = token.Register(static x => ((SyncCompletionSource)x!).SetCompleted(Unit.Default), _signalChangedAwaitable);
                    do
                    {
                        var result = await ComputeResult(token);
                        if (token.IsCancellationRequested) return;

                        if (result.TryGetValue(out var propertyValue))
                        {
                            _observer.OnNext(propertyValue);
                        }

                        await _signalChangedAwaitable;
                        _disconnectSubscription.Dispose();
                    } while (!token.IsCancellationRequested);
                }
                finally
                {
                    // It's okay to "double" dispose it
                    _disconnectSubscription.Dispose();
                }
            }
            catch
            {
                // Ignored
            }
        }

        async ValueTask<Optional<T>> ComputeResult(CancellationToken cancellationToken)
        {
            _signalsRequested.Clear();
            _signalChangedAwaitable.Reset();
            _anySignalArrived = 0;

            _disconnectSubscription = new();
            _signalChangeSubscription = new DisposableBag().AddTo(ref _disconnectSubscription);

            if (_observable._concurrentChangeStrategy == ConcurrentChangeStrategy.CancelCurrent)
            {
                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cancellationToken = _cts.Token;
                _cts.AddTo(ref _disconnectSubscription);
            }
            else
            {
                _cts = null;
            }

            var signalRequestedSubscription = Signal.SignalsRequested.Subscribe(_onSignalRequested);

            try
            {
                try
                {
                    return new(await _observable._func(cancellationToken));
                }
                finally
                {
                    signalRequestedSubscription.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                return Optional<T>.Empty;
            }
            catch
            {
                try
                {
                    return _observable._fallbackValue();
                }
                catch
                {
                    return Optional<T>.Empty;
                }
            }
        }

        public void Dispose()
        {
            _disposed.Cancel();
            _disposed.Dispose();
        }
    }

}

internal sealed class ActionStub
{
    public static readonly Action Nop = () => { };
}