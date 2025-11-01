using R3;
using SignalsDotnet.Configuration;
using SignalsDotnet.Helpers;
using SignalsDotnet.Internals;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet;
public static partial class Signal
{
    public static IAsyncReadOnlySignal<T> AsyncComputed<T>(Func<CancellationToken, ValueTask<T>> func,
                                                           T startValue,
                                                           Func<Optional<T>> fallbackValue,
                                                           ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                                           ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        func = func.TraceWhenExecuting(out var isExecuting);
        return AsyncComputed(func, new Optional<T>(startValue), fallbackValue, isExecuting, concurrentChangeStrategy, configuration!);
    }

    public static IAsyncReadOnlySignal<T> AsyncComputed<T>(Func<CancellationToken, ValueTask<T>> func,
                                                           T startValue,
                                                           Func<T> fallbackValue,
                                                           ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                                           ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return AsyncComputed(func, startValue, () => new Optional<T>(fallbackValue()), concurrentChangeStrategy, configuration);
    }

    public static IAsyncReadOnlySignal<T> AsyncComputed<T>(Func<CancellationToken, ValueTask<T>> func,
                                                           T startValue,
                                                           ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                                           ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return AsyncComputed(func, startValue, static () => Optional<T>.Empty, concurrentChangeStrategy, configuration);
    }


    public static Observable<T> AsyncComputedObservable<T>(Func<CancellationToken, ValueTask<T>> func,
                                                           T startValue,
                                                           Func<Optional<T>> fallbackValue,
                                                           ConcurrentChangeStrategy concurrentChangeStrategy = default)
    {
        return ComputedObservable(func, fallbackValue, concurrentChangeStrategy: concurrentChangeStrategy).Prepend(startValue);
    }


    internal static IAsyncSignal<T> AsyncComputed<T>(Func<CancellationToken, ValueTask<T>> func,
                                                             Optional<T> startValueOptional,
                                                             Func<Optional<T>> fallbackValue,
                                                             IReadOnlySignal<bool> isExecuting,
                                                             ConcurrentChangeStrategy concurrentChangeStrategy,
                                                             ReadonlySignalConfigurationDelegate<T?>? configuration)
    {
        var valueObservable = ComputedObservable(func, fallbackValue, null, concurrentChangeStrategy);
        if (startValueOptional.TryGetValue(out var startValue))
        {
            valueObservable = valueObservable.Prepend(startValue);
        }

        return new FromObservableAsyncSignal<T>(valueObservable, isExecuting, configuration);
    }
}
