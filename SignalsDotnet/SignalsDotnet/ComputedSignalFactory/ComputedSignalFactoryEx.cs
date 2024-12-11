using SignalsDotnet.Configuration;
using SignalsDotnet.Helpers;
using SignalsDotnet.Internals.ComputedSignalrFactory;

namespace SignalsDotnet;

public static class ComputedSignalFactoryEx
{
    public static IComputedSignalFactory DisconnectEverythingWhen(this IComputedSignalFactory @this, IObservable<bool> shouldBeCancelled)
    {
        return new CancelComputedSignalFactoryDecorator(@this, CancellationSignal.Create(shouldBeCancelled));
    }

    public static IComputedSignalFactory OnException(this IComputedSignalFactory @this, Action<Exception> onException, bool ignoreOperationCancelled = true)
    {
        return new OnErrorComputedSignalFactoryDecorator(@this, ignoreOperationCancelled, onException);
    }

    public static IReadOnlySignal<T> Computed<T>(this IComputedSignalFactory @this, Func<T> func, Func<T> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return @this.Computed(func, () => new Optional<T>(fallbackValue()), configuration);
    }

    public static IReadOnlySignal<T?> Computed<T>(this IComputedSignalFactory @this, Func<T> func, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return @this.Computed(func, static () => default, configuration);
    }

    public static IReadOnlySignal<T> AsyncComputed<T>(this IComputedSignalFactory @this,
                                                         Func<CancellationToken, ValueTask<T>> func,
                                                         T startValue,
                                                         Func<T> fallbackValue,
                                                         ConcurrentRecomputeStrategy concurrentRecomputeStrategy = default,
                                                         ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return @this.AsyncComputed(func, startValue, () => new Optional<T>(fallbackValue()), concurrentRecomputeStrategy, configuration);
    }

    public static IReadOnlySignal<T> AsyncComputed<T>(this IComputedSignalFactory @this,
                                                         Func<CancellationToken, ValueTask<T>> func,
                                                         T startValue,
                                                         ConcurrentRecomputeStrategy concurrentRecomputeStrategy = default,
                                                         ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return @this.AsyncComputed(func, startValue, static () => default, concurrentRecomputeStrategy, configuration);
    }
}