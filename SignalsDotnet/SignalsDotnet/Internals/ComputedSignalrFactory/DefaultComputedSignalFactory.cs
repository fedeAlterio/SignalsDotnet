using R3;
using SignalsDotnet.Configuration;
using SignalsDotnet.Helpers;

namespace SignalsDotnet.Internals.ComputedSignalrFactory;

internal class DefaultComputedSignalFactory : IComputedSignalFactory
{
    public IReadOnlySignal<T> Computed<T>(Func<T> func, Func<Optional<T>> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return Signal.Computed(func, fallbackValue, configuration);
    }

    public IAsyncReadOnlySignal<T> AsyncComputed<T>(Func<CancellationToken, ValueTask<T>> func,
                                               T startValue,
                                               Func<Optional<T>> fallbackValue,
                                               ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                               ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return Signal.AsyncComputed(func, startValue, fallbackValue, concurrentChangeStrategy, configuration);
    }


    public Observable<T> ComputedObservable<T>(Func<T> func, Func<Optional<T>> fallbackValue)
    {
        return Signal.ComputedObservable(func, fallbackValue);
    }

    public Observable<T> AsyncComputedObservable<T>(Func<CancellationToken, ValueTask<T>> func, T startValue, Func<Optional<T>> fallbackValue, ConcurrentChangeStrategy concurrentChangeStrategy = default)
    {
        return Signal.AsyncComputedObservable(func, startValue, fallbackValue, concurrentChangeStrategy);
    }

    public ISignal<T> Linked<T>(Func<T> func, Func<Optional<T>> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return Signal.Linked(func, fallbackValue, configuration);
    }

    public IAsyncSignal<T> AsyncLinked<T>(Func<CancellationToken, ValueTask<T>> func,
                                          T startValue,
                                          Func<Optional<T>> fallbackValue,
                                          ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                          ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return Signal.AsyncLinked(func, startValue, fallbackValue, concurrentChangeStrategy, configuration);
    }


    public Effect Effect(Action onChange, TimeProvider? scheduler)
    {
        return new Effect(onChange, scheduler);
    }

    public Effect AsyncEffect(Func<CancellationToken, ValueTask> onChange, ConcurrentChangeStrategy concurrentChangeStrategy, TimeProvider? scheduler)
    {
        return new Effect(onChange, concurrentChangeStrategy, scheduler);
    }


    public static DefaultComputedSignalFactory Instance { get; } = new();
}