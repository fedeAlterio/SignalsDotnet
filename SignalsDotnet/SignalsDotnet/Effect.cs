using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using SignalsDotnet.Helpers;

namespace SignalsDotnet;

public class Effect : IDisposable
{
    static readonly object _atomicOperationsLocker = new ();
    static readonly AsyncLocal<BehaviorSubject<int>> _atomicOperationsCounter = new();
    readonly IDisposable _subscription;

    public Effect(Action onChange, IScheduler? scheduler = null)
    {
        var computationDelayer = ComputationDelayer(scheduler ?? DefaultScheduler);
        _subscription = Signal.ComputedObservable(_ =>
                              {
                                  onChange();
                                  return ValueTask.FromResult(Unit.Default);
                              }, static () => Optional<Unit>.Empty, computationDelayer)
                              .Subscribe();
    }

    public Effect(Func<CancellationToken, ValueTask> onChange, ConcurrentRecomputeStrategy concurrentRecomputeStrategy = default,  IScheduler? scheduler = null)
    {
        var computationDelayer = ComputationDelayer(scheduler ?? DefaultScheduler);
        _subscription = Signal.ComputedObservable(async token =>
                              {
                                  await onChange(token);
                                  return Unit.Default;
                              }, static () => Optional<Unit>.Empty, computationDelayer, concurrentRecomputeStrategy)
                              .Subscribe();
    }

    static Func<Unit, IObservable<Unit>> ComputationDelayer(IScheduler? scheduler)
    {
        var atomicOperations = Observable.Defer(() =>
        {
            lock (_atomicOperationsLocker)
            {
                return _atomicOperationsCounter.Value ??= new(0);
            }
        });

        var noAtomicOperations = atomicOperations.Where(counter => counter == 0)
                                                 .Select(static _ => Unit.Default);

        return scheduler is null
            ? _ => noAtomicOperations
            : _ => noAtomicOperations.ObserveOn(scheduler);
    }

    public static void AtomicOperation(Action action)
    {
        lock (_atomicOperationsLocker)
        {
            _atomicOperationsCounter.Value ??= new(0);
            _atomicOperationsCounter.Value.OnNext(_atomicOperationsCounter.Value.Value + 1);
        }

        try
        {
            action();
        }
        finally
        {
            lock (_atomicOperationsLocker)
            {
                _atomicOperationsCounter.Value.OnNext(_atomicOperationsCounter.Value.Value - 1);
            }
        }
    }

    public static async ValueTask AtomicOperation(Func<ValueTask> action)
    {
        lock (_atomicOperationsLocker)
        {
            _atomicOperationsCounter.Value ??= new(0);
            _atomicOperationsCounter.Value.OnNext(_atomicOperationsCounter.Value.Value + 1);
        }

        try
        {
            await action();
        }
        finally
        {
            lock (_atomicOperationsLocker)
            {
                _atomicOperationsCounter.Value.OnNext(_atomicOperationsCounter.Value.Value - 1);
            }
        }
    }

    public static IScheduler? DefaultScheduler { get; set; }
    public void Dispose() => _subscription.Dispose();
}
