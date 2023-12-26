using System.Reactive;
using System.Reactive.Linq;

namespace SignalsDotnet;

public abstract partial class Signal
{
    public static IObservable<Unit> WhenAnyChanged(params IReadOnlySignal[] signals)
    {
        return WhenAnyChanged((IReadOnlyCollection<IReadOnlySignal>)signals);
    }

    public static IObservable<Unit> WhenAnyChanged(IReadOnlyCollection<IReadOnlySignal> signals)
    {
        if (signals is null)
            throw new ArgumentNullException(nameof(signals));

        if (signals.Count == 0)
            return Observable.Empty<Unit>();

        return signals.Select(x => x.Changed)
                      .Merge()
                      .Skip(signals.Count - 1); // All observable start with a value, so we skip them but one.
    }
}