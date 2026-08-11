using R3;
using SignalsDotnet.Configuration;
using SignalsDotnet.Helpers;
using SignalsDotnet.Internals;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet;

public partial class Signal
{
    public static IReadOnlySignal<T> Computed<T>(Func<T> func, Func<T> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return Computed(func.ToAsyncValueTask(), default, () => new Optional<T>(fallbackValue()), default, configuration);
    }

    public static IReadOnlySignal<T> Computed<T>(Func<T> func, Func<Optional<T>> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return Computed(func.ToAsyncValueTask(), default, fallbackValue, default, configuration);
    }

    public static IReadOnlySignal<T> Computed<T>(Func<T> func, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return Computed(func.ToAsyncValueTask(), default, static () => Optional<T>.Empty, default, configuration);
    }

    public static IReadOnlySignal<Unit> Computed(Action action)
    {
        return Computed(action.ToAsyncValueTask(), default, static () => Optional<Unit>.Empty, default, config => config with { RaiseOnlyWhenChanged = false });
    }

    public static Observable<T> ComputedObservable<T>(Func<T> func,
                                                       Func<Optional<T>> fallbackValue)
    {
        return ComputedObservable(func.ToAsyncValueTask(), fallbackValue);
    }

    public static Observable<T> ComputedObservable<T>(Func<T> func)
    {
        return ComputedObservable(func.ToAsyncValueTask(), static () => default);
    }

    public static IAwaitable<T> WaitForChangeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        var source = new SyncCompletionSource<T>();
        var registration = new SingleAssignmentDisposable();
        var completionClaimed = 0;

        bool TryClaimCompletion() => Interlocked.CompareExchange(ref completionClaimed, 1, 0) == 0;

        void OnSignalChanged()
        {
            if (!TryClaimCompletion())
                return;

            registration.Dispose();

            T value;
            try
            {
                value = Untracked(action);
            }
            catch (Exception exception)
            {
                source.Error = exception;
                source.SetCompleted();
                return;
            }

            source.SetResult(value);
        }

        using (UntrackedScope())
        using (TrackedScope(out var subscription, OnSignalChanged))
        {
            try
            {
                action();
            }
            catch
            {
                subscription.Dispose();
                throw;
            }

            if (cancellationToken.CanBeCanceled)
            {
                registration.Disposable = cancellationToken.Register(() =>
                {
                    subscription.Dispose();
                    if (!TryClaimCompletion())
                        return;

                    source.Error = new OperationCanceledException(cancellationToken);
                    source.SetCompleted();
                });
            }
        }

        return source;
    }



    internal static ISignal<T> Computed<T>(Func<CancellationToken, ValueTask<T>> func,
                                                   Optional<T> startValueOptional,
                                                   Func<Optional<T>> fallbackValue,
                                                   ConcurrentChangeStrategy concurrentChangeStrategy,
                                                   ReadonlySignalConfigurationDelegate<T?>? configuration)
    {
        var valueObservable = ComputedObservable(func, fallbackValue, null, concurrentChangeStrategy);
        if (startValueOptional.TryGetValue(out var startValue))
        {
            valueObservable = valueObservable.Prepend(startValue);
        }

        return valueObservable.ToLinkedSignal(configuration);
    }

    internal static Observable<T> ComputedObservable<T>(Func<CancellationToken, ValueTask<T>> func,
                                                        Func<Optional<T>> fallbackValue,
                                                        Func<Unit, Observable<Unit>>? scheduler = null,
                                                        ConcurrentChangeStrategy concurrentChangeStrategy = default)
    {
        return new ComputedObservable<T>(func, fallbackValue, scheduler, concurrentChangeStrategy);
    }
}