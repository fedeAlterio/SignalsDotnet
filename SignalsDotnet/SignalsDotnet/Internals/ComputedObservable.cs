using System.Runtime.CompilerServices;
using SignalsDotnet.Helpers;
using R3;

namespace SignalsDotnet.Internals;

internal class ComputedObservable<T> : Observable<T>
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

    class Subscription : IDisposable
    {
        readonly ComputedObservable<T> _observable;
        readonly Observer<T> _observer;
        readonly CancellationTokenSource _disposed = new();

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
                var token = _disposed.Token;
                while (!token.IsCancellationRequested)
                {
                    var result = await ComputeResult(_disposed.Token);
                    if (result.ResultOptional.TryGetValue(out var propertyValue))
                    {
                        _observer.OnNext(propertyValue);
                    }
                    await result.ShouldComputeNextResult;
                }
            }
            catch
            {
                // Ignored
            }
        }

        async ValueTask<ComputationResult> ComputeResult(CancellationToken cancellationToken)
        {
            var referenceEquality = ReferenceEqualityComparer.Instance;
            HashSet<IReadOnlySignal> signalRequested = new(referenceEquality);
            Optional<T> result;

            var disconnectSubscription = new DisposableBag();
            var signalChangeSubscription = new DisposableBag().AddTo(ref disconnectSubscription);
            var signalChangedAwaitable = new DisconnectOnCompletionAwaitable(disconnectSubscription);
            CancellationTokenSource? cts;
            if (_observable._concurrentChangeStrategy == ConcurrentChangeStrategy.CancelCurrent)
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cancellationToken = cts.Token;
                cts.AddTo(ref disconnectSubscription);
            }
            else
            {
                cts = null;
            }

            int anySignalArrived = 0;
            var signalRequestedSubscription = Signal.SignalsRequested()
                                                    .Subscribe(signal =>
                                                    {
                                                        if (!signalRequested.Add(signal)) return;

                                                        signal.FutureValues.Subscribe(_ =>
                                                        {
                                                            if (Interlocked.CompareExchange(ref anySignalArrived, 1, 0) == 1) return;

                                                            signalChangeSubscription.Dispose();
                                                            cts?.Cancel();
                                                            var scheduler = _observable._scheduler;
                                                            if (scheduler is not null)
                                                            {
                                                                scheduler(Unit.Default).Subscribe(signalChangedAwaitable.SetCompleted)
                                                                                       .AddTo(ref disconnectSubscription);
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

    record struct ComputationResult(DisconnectOnCompletionAwaitable ShouldComputeNextResult, Optional<T> ResultOptional);
    class DisconnectOnCompletionAwaitable(IDisposable disposable) : INotifyCompletion
    {
        readonly IDisposable _disposable = disposable;
        Action? _continuation;

        public DisconnectOnCompletionAwaitable GetAwaiter() => this;
        public bool IsCompleted => ReferenceEquals(Volatile.Read(ref _continuation), ActionStub.Nop);
        public void OnCompleted(Action continuation)
        {
            Action? original = Interlocked.CompareExchange(ref _continuation, continuation, null);
            if (original is null) return; // Normal case
            if (ReferenceEquals(original, ActionStub.Nop))
                continuation(); // Rare case
            else
                throw new InvalidOperationException("Double await");
        }

        public void GetResult() => _disposable.Dispose();

        public void SetCompleted(Unit unit)
        {
            Interlocked.Exchange(ref _continuation, ActionStub.Nop)?.Invoke();
        }
    }

    class SingleNotificationObservable<TNotification> : Observable<TNotification>, IDisposable
    {
        Observer<TNotification>? _observer;
        readonly object _locker = new();
        Optional<TNotification> _value;

        protected override IDisposable SubscribeCore(Observer<TNotification> observer)
        {
            lock (_locker)
            {
                if (_value.TryGetValue(out var value))
                {
                    observer.OnNext(value);
                    observer.OnCompleted();
                }
                else
                {
                    _observer = observer;
                }

                return this;
            }
        }

        public void SetResult(TNotification value)
        {
            lock (this)
            {
                var observer = _observer;
                if (observer is not null)
                {
                    observer.OnNext(value);
                    observer.OnCompleted();
                    return;
                }

                _value = new(value);
            }
        }

        public void Dispose() => Interlocked.Exchange(ref _observer, null);
    }
}

internal class ActionStub
{
    public static readonly Action Nop = () => { };
}