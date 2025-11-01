using SignalsDotnet.Configuration;
using SignalsDotnet.Helpers;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet;

public static partial class Signal
{
    public static IAsyncSignal<T> AsyncLinked<T>(Func<CancellationToken, ValueTask<T>> func,
                                                 T startValue, 
                                                 Func<Optional<T>> fallbackValue, 
                                                 ConcurrentChangeStrategy concurrentChangeStrategy = default, 
                                                 ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        func = func.TraceWhenExecuting(out var isExecuting);
        return AsyncComputed(func, new Optional<T>(startValue), fallbackValue, isExecuting, concurrentChangeStrategy, configuration!);
    }

    public static IAsyncSignal<T> AsyncLinked<T>(Func<CancellationToken, ValueTask<T>> func,
                                                 T startValue,
                                                 Func<T> fallbackValue,
                                                 ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                                 ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return AsyncLinked(func, startValue, () => new Optional<T>(fallbackValue()), concurrentChangeStrategy, configuration);
    }

    public static IAsyncSignal<T> AsyncLinked<T>(Func<CancellationToken, ValueTask<T>> func,
                                                 T startValue,
                                                 ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                                 ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return AsyncLinked(func, startValue, static () => Optional<T>.Empty, concurrentChangeStrategy, configuration);
    }
}
