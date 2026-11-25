using R3;
using SignalsDotnet.Helpers;
using SignalsDotnet.Internals;

namespace SignalsDotnet;

public class Effect : IDisposable
{
    static readonly AsyncLocal<BehaviorSubject<int>> _atomicOperationsCounter = new();
    static readonly Observable<Unit> _runNow = Observable.Never<Unit>().Prepend(Unit.Default);
    readonly IDisposable _subscription;

    public Effect(Action onChange, TimeProvider? scheduler = null) : this(onChange, EffectKind.SideEffect, scheduler)
    {
    }

    public Effect(Action onChange, EffectKind kind, TimeProvider? scheduler = null)
    {
        var computationDelayer = ComputationDelayer(scheduler ?? DefaultScheduler);
        _subscription = Signal.ComputedObservable(_ =>
                              {
                                  onChange();
                                  return new(Unit.Default);
                              }, static () => Optional<Unit>.Empty, computationDelayer, flushOrder: kind.FlushOrder())
                              .Subscribe();
    }

    public Effect(Func<CancellationToken, ValueTask> onChange, ConcurrentChangeStrategy concurrentChangeStrategy = default,  TimeProvider? scheduler = null)
    {
        var computationDelayer = ComputationDelayer(scheduler ?? DefaultScheduler);
        _subscription = Signal.ComputedObservable(async token =>
                              {
                                  await onChange(token);
                                  return Unit.Default;
                              }, static () => Optional<Unit>.Empty, computationDelayer, concurrentChangeStrategy, FlushOrder.AfterComputations)
                              .Subscribe();
    }

    public static Effect Create(Action onChange, TimeProvider? scheduler = null)
    {
        return new Effect(onChange, scheduler);
    }

    public static Effect Create(Func<CancellationToken, ValueTask> onChange, ConcurrentChangeStrategy concurrentChangeStrategy = default, TimeProvider? scheduler = null)
    {
        return new Effect(onChange, concurrentChangeStrategy, scheduler);
    }

    static Func<Unit, Observable<Unit>> ComputationDelayer(TimeProvider? scheduler)
    {
        var noAtomicOperations = Observable.Defer(static () => _atomicOperationsCounter.Value is { Value: not 0 } counter
                                                      ? WhenNoAtomicOperationIsRunning(counter)
                                                      : _runNow);

        return scheduler is null
            ? _ => noAtomicOperations
            : _ => noAtomicOperations.ObserveOn(scheduler);
    }

    static Observable<Unit> WhenNoAtomicOperationIsRunning(BehaviorSubject<int> counter)
    {
        return counter.Where(static running => running == 0)
                      .Select(static _ => Unit.Default);
    }

    public static void AtomicOperation(Action action)
    {
        var counter = BeginAtomicOperation();
        try
        {
            using (Signal.BatchScope())
            {
                action();
            }
        }
        finally
        {
            EndAtomicOperation(counter);
        }
    }

    public static async ValueTask AtomicOperationAsync(Func<ValueTask> action)
    {
        var counter = BeginAtomicOperation();
        try
        {
            await action();
        }
        finally
        {
            EndAtomicOperation(counter);
        }
    }

    static BehaviorSubject<int> BeginAtomicOperation()
    {
        var counter = _atomicOperationsCounter.Value ??= new(0);
        lock (counter)
        {
            counter.OnNext(counter.Value + 1);
        }

        return counter;
    }

    static void EndAtomicOperation(BehaviorSubject<int> counter)
    {
        lock (counter)
        {
            counter.OnNext(counter.Value - 1);
        }
    }

    public static TimeProvider? DefaultScheduler { get; set; }
    public void Dispose() => _subscription.Dispose();
}
