using R3;

namespace SignalsDotnet;

public static partial class Signal
{
    public static ISignal<Unit> CreateEvent() => new Signal<Unit>(config => config with { RaiseOnlyWhenChanged = false });
    public static void Track(this IReadOnlySignal<Unit> @this) => _ = @this.Value;
    public static void Invoke(this ISignal<Unit> @this) => @this.Value = Unit.Default;
}