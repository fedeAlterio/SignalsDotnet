using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet;


public class Effect : IDisposable
{
    static readonly ConcurrentDictionary<Thread, BehaviorSubject<int>> _atomicOperationsNestingByThread = new();
    readonly IDisposable _subscription;

    public Effect(Action onChange, IScheduler? scheduler = null)
    {
        var computationDelayer = ComputationDelayer(scheduler ?? DefaultScheduler);
        _subscription = Signal.ComputedObservable(() =>
                              {
                                  onChange();
                                  return Unit.Default;
                              }, static () => Optional<Unit>.Empty, computationDelayer)
                              .Subscribe();
    }

    static Func<Unit, IObservable<Unit>> ComputationDelayer(IScheduler? scheduler)
    {
        var atomicOperations = _atomicOperationsNestingByThread.GetOrAdd(Thread.CurrentThread, static _ => new BehaviorSubject<int>(0));
        var noAtomicOperations = atomicOperations.Where(counter => counter == 0)
                                                 .Select(static _ => Unit.Default);

        return scheduler is null
            ? _ => noAtomicOperations
            : _ => noAtomicOperations.ObserveOn(scheduler);
    }

    public static void AtomicOperation(Action action)
    {
        var atomicOperations = _atomicOperationsNestingByThread.AddOrUpdate(Thread.CurrentThread, static _ => new BehaviorSubject<int>(0), (_, subject) =>
        {
            subject.OnNext(subject.Value + 1);
            return subject;
        });

        try
        {
            action();
        }
        finally
        {
            atomicOperations.OnNext(atomicOperations.Value - 1);
        }
    }

    public static IScheduler? DefaultScheduler { get; set; }

    public void Dispose() => _subscription.Dispose();
}
