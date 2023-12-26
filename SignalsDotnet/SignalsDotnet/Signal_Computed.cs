using System.Reactive.Disposables;
using System.Reactive.Linq;
using SignalsDotnet.Configuration;
using SignalsDotnet.Internals;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet;

public partial class Signal
{
    internal static IReadOnlySignal<T> Computed<T>(Func<T> func, Func<Optional<T>> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration)
    {
        var valueObservable = Observable.Create<T>(observer =>
        {
            var disposable = new SerialDisposable();
            OnNewResult(ComputeResult(func, fallbackValue));
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

        return new FromObservableSignal<T>(valueObservable, configuration)!;
    }

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


    static ComputationResult<T> ComputeResult<T>(Func<T> resultFunc, Func<Optional<T>> fallbackValue)
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

        var observable = WhenAnyChanged(propertiesRequested)
                         .Skip(1) // We want to observe only future changes
                         .Select(_ => ComputeResult(resultFunc, fallbackValue));

        return new(observable, result);
    }

    record struct ComputationResult<T>(IObservable<ComputationResult<T>> NextResult, Optional<T> ResultOptional);
}