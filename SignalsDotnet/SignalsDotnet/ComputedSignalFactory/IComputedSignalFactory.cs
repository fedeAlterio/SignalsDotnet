using System.Reactive.Concurrency;
using SignalsDotnet.Configuration;
using SignalsDotnet.Helpers;

namespace SignalsDotnet;

public interface IComputedSignalFactory
{
    IReadOnlySignal<T> Computed<T>(Func<T> func, Func<Optional<T>> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration = null);
    IObservable<T> ComputedObservable<T>(Func<T> func, Func<Optional<T>> fallbackValue);

    IReadOnlySignal<T> AsyncComputed<T>(Func<CancellationToken, ValueTask<T>> func,
                                        T startValue,
                                        Func<Optional<T>> fallbackValue,
                                        ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                        ReadonlySignalConfigurationDelegate<T>? configuration = null);

    IObservable<T> AsyncComputedObservable<T>(Func<CancellationToken, ValueTask<T>> func,
                                              T startValue,
                                              Func<Optional<T>> fallbackValue,
                                              ConcurrentChangeStrategy concurrentChangeStrategy = default);

    Effect Effect(Action onChange, IScheduler? scheduler = null);
    Effect AsyncEffect(Func<CancellationToken, ValueTask> onChange, ConcurrentChangeStrategy concurrentChangeStrategy = default, IScheduler? scheduler = null);
}