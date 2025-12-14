using R3;
using SignalsDotnet.Configuration;

namespace SignalsDotnet;

public static partial class Signal
{
    internal static readonly SignalConfiguration<Unit> SignalEventConfig = SignalConfiguration<Unit>.Default with { RaiseOnlyWhenChanged = false };
    public static ISignal<Unit> CreateEvent() => new Signal<Unit>(static _ => SignalEventConfig);
    public static void Track(this IReadOnlySignal<Unit> @this) => _ = @this.Value;
    public static void Invoke(this ISignal<Unit> @this) => @this.Value = Unit.Default;
}