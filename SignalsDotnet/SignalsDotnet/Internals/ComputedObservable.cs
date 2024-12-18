using SignalsDotnet.Helpers;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive;
using ObservableEx = SignalsDotnet.Internals.Helpers.ObservableEx;

namespace SignalsDotnet.Internals;

internal class ComputedObservable<T> : IObservable<T>
{
    readonly Func<CancellationToken, ValueTask<T>> _func;
    readonly Func<Optional<T>> _fallbackValue;
    readonly Func<Unit, IObservable<Unit>>? _scheduler;
    readonly ConcurrentChangeStrategy _concurrentChangeStrategy;

    public ComputedObservable(Func<CancellationToken, ValueTask<T>> func,
                              Func<Optional<T>> fallbackValue,
                              Func<Unit, IObservable<Unit>>? scheduler = null,
                              ConcurrentChangeStrategy concurrentChangeStrategy = default)
    {
        _func = func;
        _fallbackValue = fallbackValue;
        _scheduler = scheduler;
        _concurrentChangeStrategy = concurrentChangeStrategy;
    }

    public IDisposable Subscribe(IObserver<T> observer) => new Subscription(this, observer);

    class Subscription : IDisposable
    {
        readonly ComputedObservable<T> _observable;
        readonly IObserver<T> _observer;
        readonly MultipleAssignmentDisposable _disposable = new();
        public Subscription(ComputedObservable<T> observable, IObserver<T> observer)
        {
            _observable = observable;
            _observer = observer;
            _disposable.Disposable = ObservableEx.FromAsyncUsingAsyncContext(ComputeResult)
                                                 .Take(1)
                                                 .Subscribe(OnNewResult);
        }

        void OnNewResult(ComputationResult result)
        {
            var valueNotified = false;

            _disposable.Disposable = result.ShouldComputeNextResult
                                           .Take(1)
                                           .SelectMany(_ =>
                                           {
                                               NotifyValueIfNotAlready();
                                               return ObservableEx.FromAsyncUsingAsyncContext(ComputeResult);
                                           })
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
            BehaviorSubject<bool> stopListeningForSignals = new(false);

            var signalChangedObservable = Signal.SignalsRequested()
                                                .TakeUntil(stopListeningForSignals.Where(x => x))
                                                .Where(x => signalRequested.Add(x))
                                                .Select(x => x.Changed.Skip(1))
                                                .Merge()
                                                .Take(1);

            if (_observable._concurrentChangeStrategy == ConcurrentChangeStrategy.CancelCurrent)
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cancellationToken = cts.Token;
                signalChangedObservable = signalChangedObservable.Do(_ => cts.Cancel())
                                                                 .Finally(cts.Dispose);
            }

            var scheduler = _observable._scheduler;
            if (scheduler is not null)
            {
                signalChangedObservable = signalChangedObservable.Select(scheduler)
                                                                 .Switch();
            }

            var shouldComputeNextResult = signalChangedObservable.Replay(1);

            var disconnect = shouldComputeNextResult.Connect();

            try
            {
                try
                {
                    result = new(await _observable._func(cancellationToken));
                }
                finally
                {
                    stopListeningForSignals.OnNext(true);
                    stopListeningForSignals.OnCompleted();
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

            var resultObservable = new DisconnectOnDisposeObservable<Unit>(shouldComputeNextResult, Disposable.Create(() =>
            {
                disconnect.Dispose();
            }));

            return new(resultObservable, result);
        }


        public void Dispose() => _disposable.Dispose();
    }

    record struct ComputationResult(IObservable<Unit> ShouldComputeNextResult, Optional<T> ResultOptional);
    class DisconnectOnDisposeObservable<TV> : IObservable<TV>
    {
        readonly IObservable<TV> _observable;
        readonly IDisposable _disconnect;

        public DisconnectOnDisposeObservable(IObservable<TV> observable, IDisposable disconnect)
        {
            _observable = observable;
            _disconnect = disconnect;
        }

        public IDisposable Subscribe(IObserver<TV> observer)
        {
            _observable.Subscribe(observer);
            return Disposable.Create(() => _disconnect.Dispose());
        }
    }
}