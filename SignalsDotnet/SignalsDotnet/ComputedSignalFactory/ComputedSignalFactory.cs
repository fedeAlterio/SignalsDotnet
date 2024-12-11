using SignalsDotnet.Internals.ComputedSignalrFactory;

namespace SignalsDotnet;

public static class ComputedSignalFactory
{
    public static IComputedSignalFactory Default => DefaultComputedSignalFactory.Instance;
}