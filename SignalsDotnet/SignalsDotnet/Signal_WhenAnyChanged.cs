using R3;

namespace SignalsDotnet;

public static partial class Signal
{
    public static Observable<Unit> WhenAnyChanged(params IReadOnlySignal[] signals)
    {
        return WhenAnyChanged((IReadOnlyCollection<IReadOnlySignal>)signals);
    }

    public static Observable<Unit> WhenAnyChanged(IReadOnlyCollection<IReadOnlySignal> signals)
    {
        if (signals is null)
            throw new ArgumentNullException(nameof(signals));

        if (signals.Count == 0)
            return Observable.Empty<Unit>();

        return signals.Select(x => x.FutureValues)
                      .Merge()
                      .Prepend(Unit.Default);
    }
}