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
        readonly BehaviorSubject<bool> _disposed = new(false);

        public Subscription(ComputedObservable<T> observable, Observer<T> observer)
        {
            _observable = observable;
            _observer = observer;
            Observable.FromAsync(ComputeResult)
                      .Take(1)
                      .TakeUntil(_disposed.Where(x => x))
                      .Subscribe(OnNewResult);
        }

        void OnNewResult(ComputationResult result)
        {
            var valueNotified = false;

            result.ShouldComputeNextResult
                  .Take(1)
                  .SelectMany(_ =>
                  {
                      NotifyValueIfNotAlready();
                      return Observable.FromAsync(ComputeResult);
                  })
                  .TakeUntil(_disposed.Where(x => x))
                  .Subscribe(OnNewResult);

            NotifyValueIfNotAlready();

            // We notify a new value only if the func() evaluation succeeds.
            void NotifyValueIfNotAlready()
            {
                if (valueNotified)
                    return;

                valueNotified = true;
                if (result.ResultOptional.TryGetValue(out var propertyValue))
                {
                    _observer.OnNext(propertyValue);
                }
            }
        }

        async ValueTask<ComputationResult> ComputeResult(CancellationToken cancellationToken)
        {
            var referenceEquality = ReferenceEqualityComparer.Instance;
            HashSet<IReadOnlySignal> signalRequested = new(referenceEquality);
            Optional<T> result;
            var signalChanged = new SingleNotificationObservable<Unit>();

            var disconnectSubscription = new DisposableBag();
            var signalChangeSubscription = new DisposableBag().AddTo(ref disconnectSubscription);
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
                                                                scheduler(Unit.Default).Subscribe(signalChanged.SetResult)
                                                                                       .AddTo(ref disconnectSubscription);
                                                            }
                                                            else
                                                            {
                                                                signalChanged.SetResult(Unit.Default);
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

            var resultObservable = new DisconnectOnDisposeObservable<Unit>(signalChanged, disconnectSubscription);

            return new(resultObservable, result);
        }

        public void Dispose() => _disposed.OnNext(true);
    }

    record struct ComputationResult(Observable<Unit> ShouldComputeNextResult, Optional<T> ResultOptional);
    class DisconnectOnDisposeObservable<TV> : Observable<TV>
    {
        readonly Observable<TV> _observable;
        readonly IDisposable _disconnect;

        public DisconnectOnDisposeObservable(Observable<TV> observable, IDisposable disconnect)
        {
            _observable = observable;
            _disconnect = disconnect;
        }

        protected override IDisposable SubscribeCore(Observer<TV> observer)
        {
            _observable.Subscribe(observer.OnNext, observer.OnErrorResume, observer.OnCompleted);
            return _disconnect;
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