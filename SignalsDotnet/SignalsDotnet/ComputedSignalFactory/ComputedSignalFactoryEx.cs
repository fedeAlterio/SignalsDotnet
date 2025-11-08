using R3;
using SignalsDotnet.Configuration;
using SignalsDotnet.Helpers;
using SignalsDotnet.Internals.ComputedSignalrFactory;

namespace SignalsDotnet;

public static class ComputedSignalFactoryEx
{
    public static IComputedSignalFactory DisconnectEverythingWhen(this IComputedSignalFactory @this, Observable<bool> shouldBeCancelled)
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

    public static IReadOnlySignal<T> Computed<T>(this IComputedSignalFactory @this, Func<T> func, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return @this.Computed(func, static () => default, configuration);
    }

    public static IReadOnlySignal<Unit> Computed(this IComputedSignalFactory @this, Action action)
    {
        return @this.Computed(() =>
        {
            action();
            return Unit.Default;
        }, static () => Optional<Unit>.Empty , config => config with { RaiseOnlyWhenChanged = false });
    }

    public static IAsyncReadOnlySignal<T> AsyncComputed<T>(this IComputedSignalFactory @this,
                                                         Func<CancellationToken, ValueTask<T>> func,
                                                         T startValue,
                                                         Func<T> fallbackValue,
                                                         ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                                         ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return @this.AsyncComputed(func, startValue, () => new Optional<T>(fallbackValue()), concurrentChangeStrategy, configuration);
    }

    public static IAsyncReadOnlySignal<Unit> AsyncComputed(this IComputedSignalFactory @this,
                                                           Func<CancellationToken, ValueTask> action,
                                                           ConcurrentChangeStrategy concurrentChangeStrategy = default)
    {
        return @this.AsyncComputed(async token =>
        {
            await action(token);
            return Unit.Default;
        }, Unit.Default, static () => Optional<Unit>.Empty, concurrentChangeStrategy, config => config with{RaiseOnlyWhenChanged = false});
    }

    public static IAsyncReadOnlySignal<T> AsyncComputed<T>(this IComputedSignalFactory @this,
                                                           Func<CancellationToken, ValueTask<T>> func,
                                                           T startValue,
                                                           ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                                           ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return @this.AsyncComputed(func, startValue, static () => default, concurrentChangeStrategy, configuration);
    }

    public static ISignal<T> Linked<T>(this IComputedSignalFactory @this, Func<T> func, Func<T> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return @this.Linked(func, () => new Optional<T>(fallbackValue()), configuration);
    }

    public static ISignal<T> Linked<T>(this IComputedSignalFactory @this, Func<T> func, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return @this.Linked(func, static () => default, configuration);
    }

    public static IAsyncSignal<T> AsyncLinked<T>(this IComputedSignalFactory @this,
                                                  Func<CancellationToken, ValueTask<T>> func,
                                                  T startValue,
                                                  Func<T> fallbackValue,
                                                  ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                                  ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return @this.AsyncLinked(func, startValue, () => new Optional<T>(fallbackValue()), concurrentChangeStrategy, configuration);
    }

    public static IAsyncSignal<T> AsyncLinked<T>(this IComputedSignalFactory @this,
                                                  Func<CancellationToken, ValueTask<T>> func,
                                                  T startValue,
                                                  ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                                  ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return @this.AsyncLinked(func, startValue, static () => default, concurrentChangeStrategy, configuration);
    }
}