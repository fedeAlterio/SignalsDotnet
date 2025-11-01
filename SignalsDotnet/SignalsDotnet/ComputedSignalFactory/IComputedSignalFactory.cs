using R3;
using SignalsDotnet.Configuration;
using SignalsDotnet.Helpers;

namespace SignalsDotnet;

public interface IComputedSignalFactory
{
    IReadOnlySignal<T> Computed<T>(Func<T> func, Func<Optional<T>> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration = null);
    Observable<T> ComputedObservable<T>(Func<T> func, Func<Optional<T>> fallbackValue);

    IAsyncReadOnlySignal<T> AsyncComputed<T>(Func<CancellationToken, ValueTask<T>> func,
                                             T startValue,
                                             Func<Optional<T>> fallbackValue,
                                             ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                             ReadonlySignalConfigurationDelegate<T>? configuration = null);

    Observable<T> AsyncComputedObservable<T>(Func<CancellationToken, ValueTask<T>> func,
                                             T startValue,
                                             Func<Optional<T>> fallbackValue,
                                             ConcurrentChangeStrategy concurrentChangeStrategy = default);

    ISignal<T> Linked<T>(Func<T> func, Func<Optional<T>> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration = null);

    IAsyncSignal<T> AsyncLinked<T>(Func<CancellationToken, ValueTask<T>> func,
                                   T startValue,
                                   Func<Optional<T>> fallbackValue,
                                   ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                   ReadonlySignalConfigurationDelegate<T>? configuration = null);

    Effect Effect(Action onChange, TimeProvider? scheduler = null);
    Effect AsyncEffect(Func<CancellationToken, ValueTask> onChange, ConcurrentChangeStrategy concurrentChangeStrategy = default, TimeProvider? scheduler = null);
}