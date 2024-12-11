using System.Reactive.Concurrency;
using SignalsDotnet.Configuration;
using SignalsDotnet.Helpers;

namespace SignalsDotnet.Internals.ComputedSignalrFactory;

internal class DefaultComputedSignalFactory : IComputedSignalFactory
{
    public IReadOnlySignal<T> Computed<T>(Func<T> func, Func<Optional<T>> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return Signal.Computed(func, fallbackValue, configuration);
    }

    public IReadOnlySignal<T> AsyncComputed<T>(Func<CancellationToken, ValueTask<T>> func,
                                               T startValue,
                                               Func<Optional<T>> fallbackValue,
                                               ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                               ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return Signal.AsyncComputed(func, startValue, fallbackValue, concurrentChangeStrategy, configuration);
    }


    public IObservable<T> ComputedObservable<T>(Func<T> func, Func<Optional<T>> fallbackValue)
    {
        return Signal.ComputedObservable(func, fallbackValue);
    }

    public IObservable<T> AsyncComputedObservable<T>(Func<CancellationToken, ValueTask<T>> func, T startValue, Func<Optional<T>> fallbackValue, ConcurrentChangeStrategy concurrentChangeStrategy = default)
    {
        return Signal.AsyncComputedObservable(func, startValue, fallbackValue, concurrentChangeStrategy);
    }


    public Effect Effect(Action onChange, IScheduler? scheduler)
    {
        return new Effect(onChange, scheduler);
    }

    public Effect AsyncEffect(Func<CancellationToken, ValueTask> onChange, ConcurrentChangeStrategy concurrentChangeStrategy, IScheduler? scheduler)
    {
        return new Effect(onChange, concurrentChangeStrategy, scheduler);
    }


    public static DefaultComputedSignalFactory Instance { get; } = new();
}