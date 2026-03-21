using SignalsDotnet.Internals.ComputedSignalsFactory;

namespace SignalsDotnet;

public static class ComputedSignalFactory
{
    public static IComputedSignalFactory Default => DefaultComputedSignalFactory.Instance;
}