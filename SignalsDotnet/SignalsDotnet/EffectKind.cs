using SignalsDotnet.Internals;

namespace SignalsDotnet;

public enum EffectKind
{
    SideEffect,
    Computation
}

internal static class EffectKindEx
{
    public static FlushOrder FlushOrder(this EffectKind kind) => kind == EffectKind.Computation ? Internals.FlushOrder.WithComputations : Internals.FlushOrder.AfterComputations;
}
