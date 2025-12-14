using R3;
using SignalsDotnet.Configuration;
using SignalsDotnet.Helpers;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet.Internals.ComputedSignalrFactory;

internal sealed class OnErrorComputedSignalFactoryDecorator(IComputedSignalFactory parent, bool ignoreOperationCancelled, Action<Exception> onException) : IComputedSignalFactory
{
    public IReadOnlySignal<T> Computed<T>(Func<T> func, Func<Optional<T>> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return ComputedObservable(func, fallbackValue).ToSignal(configuration!);
    }

    public Observable<T> ComputedObservable<T>(Func<T> func, Func<Optional<T>> fallbackValue)
    {
        return parent.ComputedObservable(() =>
        {
            try
            {
                return func();
            }
            catch (Exception e)
            {
                NotifyException(e);
                throw;
            }
        }, fallbackValue);
    }

    public IAsyncReadOnlySignal<T> AsyncComputed<T>(Func<CancellationToken, ValueTask<T>> func, T startValue, Func<Optional<T>> fallbackValue, ConcurrentChangeStrategy concurrentChangeStrategy = default, ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        func = func.TraceWhenExecuting(out var isExecuting);
        return AsyncComputedObservable(func, startValue, fallbackValue, concurrentChangeStrategy).ToAsyncSignal(isExecuting, configuration!);
    }

    public Observable<T> AsyncComputedObservable<T>(Func<CancellationToken, ValueTask<T>> func, T startValue, Func<Optional<T>> fallbackValue, ConcurrentChangeStrategy concurrentChangeStrategy = default)
    {
        return parent.AsyncComputedObservable(async token =>
        {
            try
            {
                return await func(token);
            }
            catch (Exception e)
            {
                NotifyException(e);
                throw;
            }
        }, startValue, fallbackValue, concurrentChangeStrategy);
    }

    public ISignal<T> Linked<T>(Func<T> func, Func<Optional<T>> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return parent.Linked(() =>
        {
            try
            {
                return func();
            }
            catch (Exception e)
            {
                NotifyException(e);
                throw;
            }
        }, fallbackValue, configuration);
    }

    public IAsyncSignal<T> AsyncLinked<T>(Func<CancellationToken, ValueTask<T>> func,
                                          T startValue,
                                          Func<Optional<T>> fallbackValue,
                                          ConcurrentChangeStrategy concurrentChangeStrategy = default,
                                          ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return parent.AsyncLinked(async token =>
        {
            try
            {
                return await func(token);
            }
            catch (Exception e)
            {
                NotifyException(e);
                throw;
            }
        }, startValue, fallbackValue, concurrentChangeStrategy, configuration);
    }

    public Effect Effect(Action onChange, TimeProvider? scheduler = null)
    {
        return parent.Effect(() =>
        {
            try
            {
                onChange();
            }
            catch (Exception e)
            {
                NotifyException(e);
                throw;
            }
        }, scheduler);
    }

    public Effect AsyncEffect(Func<CancellationToken, ValueTask> onChange, ConcurrentChangeStrategy concurrentChangeStrategy = default, TimeProvider? scheduler = null)
    {
        return parent.AsyncEffect(async token =>
        {
            try
            {
                await onChange(token);
            }
            catch (Exception e)
            {
                NotifyException(e);
                throw;
            }
        }, concurrentChangeStrategy);
    }

    void NotifyException(Exception e)
    {
        if (ignoreOperationCancelled && e is OperationCanceledException)
        {
            return;
        }

        using (Signal.UntrackedScope())
        {
            onException(e);
        }
    }
}
