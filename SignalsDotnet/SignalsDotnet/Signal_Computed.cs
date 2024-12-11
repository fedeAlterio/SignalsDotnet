﻿using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using SignalsDotnet.Configuration;
using SignalsDotnet.Helpers;
using SignalsDotnet.Internals;
using SignalsDotnet.Internals.Helpers;
using ObservableEx = SignalsDotnet.Internals.Helpers.ObservableEx;

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

    public static IReadOnlySignal<T?> Computed<T>(Func<T> func, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return Computed(func.ToAsyncValueTask(), default, static () => Optional<T>.Empty, default, configuration);
    }


    public static IReadOnlySignal<T> AsyncComputed<T>(Func<CancellationToken, ValueTask<T>> func, 
                                                      T startValue,
                                                      Func<Optional<T>> fallbackValue, 
                                                      ConcurrentRecomputeStrategy concurrentRecomputeStrategy = default,
                                                      ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return Computed(func, new Optional<T>(startValue), fallbackValue, concurrentRecomputeStrategy, configuration!);
    }

    public static IReadOnlySignal<T> AsyncComputed<T>(Func<CancellationToken, ValueTask<T>> func,
                                                      T startValue,
                                                      Func<T> fallbackValue,
                                                      ConcurrentRecomputeStrategy concurrentRecomputeStrategy = default,
                                                      ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return AsyncComputed(func, startValue, () => new Optional<T>(fallbackValue()), concurrentRecomputeStrategy, configuration);
    }

    public static IReadOnlySignal<T> AsyncComputed<T>(Func<CancellationToken, ValueTask<T>> func,
                                                      T startValue,
                                                      ConcurrentRecomputeStrategy concurrentRecomputeStrategy = default,
                                                      ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return AsyncComputed(func, startValue, static () => Optional<T>.Empty, concurrentRecomputeStrategy, configuration);
    }

    

    public static IObservable<T> ComputedObservable<T>(Func<T> func,
                                                       Func<Optional<T>> fallbackValue)
    {
        return ComputedObservable(func.ToAsyncValueTask(), fallbackValue);
    }


    public static IObservable<T> AsyncComputedObservable<T>(Func<CancellationToken, ValueTask<T>> func,
                                                            T startValue,
                                                            Func<Optional<T>> fallbackValue,
                                                            ConcurrentRecomputeStrategy concurrentRecomputeStrategy = default)
    {
        return ComputedObservable(func, fallbackValue, concurrentRecomputeStrategy: concurrentRecomputeStrategy).StartWith(startValue);
    }


    internal static IReadOnlySignal<T> Computed<T>(Func<CancellationToken, ValueTask<T>> func,
                                                   Optional<T> startValueOptional,
                                                   Func<Optional<T>> fallbackValue,
                                                   ConcurrentRecomputeStrategy concurrentRecomputeStrategy,
                                                   ReadonlySignalConfigurationDelegate<T?>? configuration)
    {
        var valueObservable = ComputedObservable(func, fallbackValue, null, concurrentRecomputeStrategy);
        if (startValueOptional.TryGetValue(out var startValue))
        {
            valueObservable = valueObservable.StartWith(startValue);
        }

        return new FromObservableSignal<T>(valueObservable, configuration)!;
    }

    internal static IObservable<T> ComputedObservable<T>(Func<CancellationToken, ValueTask<T>> func,
                                                         Func<Optional<T>> fallbackValue,
                                                         Func<Unit, IObservable<Unit>>? scheduler = null,
                                                         ConcurrentRecomputeStrategy concurrentRecomputeStrategy = default)
    {
        if (func is null)
            throw new ArgumentNullException(nameof(func));

        if (fallbackValue is null)
            throw new ArgumentNullException(nameof(fallbackValue));

        return Observable.Create<T>(observer =>
        {
            var isDisposed = Subject.Synchronize(new BehaviorSubject<bool>(false));

            ObservableEx.FromAsyncUsingAsyncContext(async token => await ComputeResult(func, fallbackValue, scheduler, concurrentRecomputeStrategy, token))
                        .TakeUntil(isDisposed.Where(x => x))
                        .Subscribe(OnNewResult);

            void OnNewResult(ComputationResult<T> result)
            {
                var nextResultComputationStarted = false;
                result.ShouldComputeNextResult.SelectMany(_ =>
                      {
                          nextResultComputationStarted = true;
                          return ObservableEx.FromAsyncUsingAsyncContext(async token => await ComputeResult(func, fallbackValue, scheduler, concurrentRecomputeStrategy, token));
                      })
                      .Take(1)
                      .TakeUntil(isDisposed.Where(x => x))
                      .Subscribe(OnNewResult);

                if (nextResultComputationStarted)
                {
                    return;
                }

                // We notify a new value only if the func() evaluation succeeds.
                if (result.ResultOptional.TryGetValue(out var propertyValue))
                    observer.OnNext(propertyValue);
            }

            return () => isDisposed.OnNext(true);
        });
    }


    static async ValueTask<ComputationResult<T>> ComputeResult<T>(Func<CancellationToken, ValueTask<T>> resultFunc,
                                                 Func<Optional<T>> fallbackValue,
                                                 Func<Unit, IObservable<Unit>>? scheduler,
                                                 ConcurrentRecomputeStrategy concurrentRecomputeStrategy,
                                                 CancellationToken cancellationToken)
    {
        var referenceEquality = ReferenceEqualityComparer.Instance;
        HashSet<IReadOnlySignal> signalRequested = new(referenceEquality);
        Optional<T> result;

        var signalChangedObservable = SignalsRequested().Where(x => signalRequested.Add(x))
                                                        .Select(x => x.Changed.Skip(1))
                                                        .Merge();

        if (concurrentRecomputeStrategy == ConcurrentRecomputeStrategy.CancelCurrent)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellationToken = cts.Token;
            signalChangedObservable = signalChangedObservable.Do(_ => cts.Cancel())
                                                             .Finally(cts.Dispose);
        }


        if (scheduler is not null)
        {
            signalChangedObservable = signalChangedObservable.Select(scheduler)
                                                             .Switch();
        }

        var shouldComputeNextResult = signalChangedObservable.Take(1)
                                                             .Replay(1);


        var disconnect = shouldComputeNextResult.Connect();

        try
        {
            result = new(await resultFunc(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            result = Optional<T>.Empty;
        }
        catch
        {
            // If something fails, the property will have the previous result,
            // We still have to observe for the properties to change (maybe next time the exception will not be thrown)
            try
            {
                result = fallbackValue();
            }
            catch
            {
                result = Optional<T>.Empty;
            }
        }

        var resultObservable = Observable.Create<Unit>(observer =>
        {
            shouldComputeNextResult.Subscribe(observer);
            return disconnect;
        });

        return new(resultObservable, result);
    }

    record struct ComputationResult<T>(IObservable<Unit> ShouldComputeNextResult, Optional<T> ResultOptional);
}
