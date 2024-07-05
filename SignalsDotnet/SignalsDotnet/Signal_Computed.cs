using System;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using SignalsDotnet.Configuration;
using SignalsDotnet.Internals;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet;

public partial class Signal
{
    public static IReadOnlySignal<T> Computed<T>(Func<T> func, Func<T> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        if (func is null)
            throw new ArgumentNullException(nameof(func));

        if (fallbackValue is null)
            throw new ArgumentNullException(nameof(fallbackValue));

        return Computed(func, () => new Optional<T>(fallbackValue()), configuration);
    }

    public static IReadOnlySignal<T?> Computed<T>(Func<T> func, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        if (func is null)
            throw new ArgumentNullException(nameof(func));

        return Computed(func, static () => Optional<T>.Empty, configuration);
    }

    internal static IObservable<T> ComputedObservable<T>(Func<T> func, IScheduler? scheduler = null)
    {
        return ComputedObservable(func, static () => Optional<T>.Empty, scheduler);
    }

    internal static IObservable<T> ComputedObservable<T>(Func<T> func, Func<T> fallbackValue, IScheduler? scheduler = null)
    {
        if (func is null)
            throw new ArgumentNullException(nameof(func));

        return ComputedObservable(func, () => new(fallbackValue()), scheduler);
    }

    internal static IReadOnlySignal<T> Computed<T>(Func<T> func, Func<Optional<T>> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration)
    {
        var valueObservable = ComputedObservable(func, fallbackValue, ImmediateScheduler.Instance);

        return new FromObservableSignal<T>(valueObservable, configuration)!;
    }

    internal static IObservable<T> ComputedObservable<T>(Func<T> func, Func<Optional<T>> fallbackValue, IScheduler? scheduler = null)
    {
        return Observable.Create<T>(observer =>
        {
            var disposable = new SerialDisposable();
            OnNewResult(ComputeResult(func, fallbackValue, scheduler));
            void OnNewResult(ComputationResult<T> result)
            {
                disposable.Disposable = result.NextResult
                                              .Take(1)
                                              .Subscribe(OnNewResult);

                // We notify a new value only if the func() evaluation succeeds.
                if (result.ResultOptional.TryGetValue(out var propertyValue))
                    observer.OnNext(propertyValue);
            }

            return disposable;
        });
    }


    static ComputationResult<T> ComputeResult<T>(Func<T> resultFunc, Func<Optional<T>> fallbackValue, IScheduler? scheduler)
    {
        var referenceEquality = ReferenceEqualityComparer.Instance;
        HashSet<IReadOnlySignal> propertiesRequested = new(referenceEquality);
        Optional<T> result;
        using (PropertiesRequested(Thread.CurrentThread).Subscribe(x => propertiesRequested.Add(x)))
        {
            try
            {
                result = new(resultFunc());
            }
            catch
            {
                // If something fails, the property will have the previous result,
                // We still have to observe for the properties to change (maybe next time the exception will not be thrown)
                result = fallbackValue();
            }
        }


        var observable = WhenAnyChanged(propertiesRequested).Skip(1); // We want to observe only future changes

        if (scheduler is not null)
        {
            observable = observable.Select(x => Observable.Return(x, scheduler))
                                   .Switch();
        }

        var restulObsrvable = observable.Select(_ => ComputeResult(resultFunc, fallbackValue, scheduler));

        return new(restulObsrvable, result);
    }

    record struct ComputationResult<T>(IObservable<ComputationResult<T>> NextResult, Optional<T> ResultOptional);
}