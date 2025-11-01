using SignalsDotnet.Configuration;
using SignalsDotnet.Helpers;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet;

public static partial class Signal
{
    public static ISignal<T> Linked<T>(Func<T> func, Func<Optional<T>> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return Computed(func.ToAsyncValueTask(), default, fallbackValue, default, configuration);
    }

    public static ISignal<T> Linked<T>(Func<T> func, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return Computed(func.ToAsyncValueTask(), default, static () => Optional<T>.Empty, default, configuration);
    }
}
