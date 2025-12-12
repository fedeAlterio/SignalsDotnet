using R3;
using SignalsDotnet.Helpers;
using System.Runtime.CompilerServices;

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

        public Subscription(ComputedObservable<T> observable, Observer<T> observer)
        {
            _observable = observable;
            _observer = observer;
            WatchSignalsChanges();
        }

        async void WatchSignalsChanges()
        {
            try
            {
                try
                {
                    var token = _disposed.Token;
                    do
                    {
                        var result = await ComputeResult(token);
                        if (token.IsCancellationRequested) return;

                        if (result.ResultOptional.TryGetValue(out var propertyValue))
                        {
                            _observer.OnNext(propertyValue);
                        }

                        await using var _ = token.Register(static x => ((SyncCompletionSource)x!).SetCompleted(Unit.Default), result.SignalChangedAwaitable);
                        await result.SignalChangedAwaitable;
                        _disconnectSubscription.Dispose();
                    } while (!token.IsCancellationRequested);
                }
                finally
                {
                    // It's okay "double" dispose it
                    _disconnectSubscription.Dispose();
                }
            }
            catch
            {
                // Ignored
            }
        }

        async ValueTask<ComputationResult> ComputeResult(CancellationToken cancellationToken)
        {
            var signalRequested = new List<IReadOnlySignal>();
            Optional<T> result;

            _disconnectSubscription = new();
            var signalChangeSubscription = new DisposableBag().AddTo(ref _disconnectSubscription);
            var signalChangedAwaitable = new SyncCompletionSource();
            CancellationTokenSource? cts;
            if (_observable._concurrentChangeStrategy == ConcurrentChangeStrategy.CancelCurrent)
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cancellationToken = cts.Token;
                cts.AddTo(ref _disconnectSubscription);
            }
            else
            {
                cts = null;
            }

            int anySignalArrived = 0;
            var signalRequestedSubscription = Signal.SignalsRequested()
                                                    .Subscribe(signal =>
                                                    {
                                                        if (signalRequested.Contains(signal, ReferenceEqualityComparer.Instance)) return;
                                                        signalRequested.Add(signal);

                                                        signal.FutureValues.Subscribe(_ =>
                                                        {
                                                            if (Interlocked.CompareExchange(ref anySignalArrived, 1, 0) == 1) return;

                                                            signalChangeSubscription.Dispose();
                                                            cts?.Cancel();
                                                            var scheduler = _observable._scheduler;
                                                            if (scheduler is not null)
                                                            {
                                                                scheduler(Unit.Default).Subscribe(signalChangedAwaitable.SetCompleted)
                                                                                       .AddTo(ref _disconnectSubscription);
                                                            }
                                                            else
                                                            {
                                                                signalChangedAwaitable.SetCompleted(Unit.Default);
                                                            }
                                                        }).AddTo(ref signalChangeSubscription);
                                                    });

            try
            {
                try
                {
                    result = new(await _observable._func(cancellationToken));
                }
                finally
                {
                    signalRequestedSubscription.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                result = Optional<T>.Empty;
            }
            catch
            {
                // If something fails, the property will have the previous result,
                // We still have to observe for the properties to change (maybe next time the exception will not be thrown)
                try
                {
                    result = _observable._fallbackValue();
                }
                catch
                {
                    result = Optional<T>.Empty;
                }
            }


            return new(signalChangedAwaitable, result);
        }


        public void Dispose()
        {
            _disposed.Cancel();
            _disposed.Dispose();
        }
    }

    readonly record struct ComputationResult(SyncCompletionSource SignalChangedAwaitable, Optional<T> ResultOptional);
    sealed class SyncCompletionSource : INotifyCompletion
    {
        Action? _continuation;
        public SyncCompletionSource GetAwaiter() => this;
        public bool IsCompleted => ReferenceEquals(Volatile.Read(ref _continuation), ActionStub.Nop);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnCompleted(Action continuation)
        {
            Action? original = Interlocked.CompareExchange(ref _continuation, continuation, null);
            if (original is null) return; 
            if (ReferenceEquals(original, ActionStub.Nop))
                continuation(); 
            else
                throw new InvalidOperationException("Double await");
        }

        public void GetResult() {}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetCompleted(Unit unit) => Interlocked.Exchange(ref _continuation, ActionStub.Nop)?.Invoke();
    }
}

internal sealed class ActionStub
{
    public static readonly Action Nop = () => { };
}