using System.Reactive;
using System.Reactive.Concurrency;

namespace SignalsDotnet;
public class Effect : IDisposable
{
    readonly IDisposable _subscription;
    public Effect(Action onChange, IScheduler? scheduler = null)
    {
        scheduler ??= DefaultScheduler;
        _subscription = Signal.ComputedObservable(() =>
        {
            onChange();
            return Unit.Default;
        }, scheduler).Subscribe();
    }
    public static IScheduler? DefaultScheduler { get; set; }

    public void Dispose() => _subscription.Dispose();
}
