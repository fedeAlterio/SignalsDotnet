using System.Collections.Specialized;
using R3;
using SignalsDotnet.Configuration;
using SignalsDotnet.Internals;

namespace SignalsDotnet;

public partial class Signal
{
    public static Signal<T> Create<T>(SignalConfigurationDelegate<T>? configurator = null)
    {
        return new Signal<T>(configurator!);
    }

    public static Signal<T> Create<T>(T startValue, SignalConfigurationDelegate<T>? configurator = null)
    {
        return new Signal<T>(startValue, configurator);
    }

    public static IReadOnlySignal<TCollection> FromObservableCollection<TCollection>(TCollection observableCollection, CollectionChangedSignalConfigurationDelegate? configurator = null) where TCollection : INotifyCollectionChanged
    {
        return new FromObservableCollectionSignal<TCollection>(observableCollection, configurator);
    }

    public static CollectionSignal<TCollection> CreateCollectionSignal<TCollection>(CollectionChangedSignalConfigurationDelegate? collectionChangedConfiguration = null,
                                                                                    SignalConfigurationDelegate<IReadOnlySignal<TCollection>?>? propertyChangedConfiguration = null) where TCollection : class, INotifyCollectionChanged
    {
        return new CollectionSignal<TCollection>(collectionChangedConfiguration, propertyChangedConfiguration);
    }

    public static IReadOnlySignal<T> FromObservable<T>(Observable<T> observable,
                                                       ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return observable.ToSignal(configuration);
    }
}

public static class SignalFactoryExtensions
{
    public static IReadOnlySignal<T> ToSignal<T>(this Observable<T> @this,
                                                  ReadonlySignalConfigurationDelegate<T?>? configurator = null)
    {
        return CreateSignal(@this, ResolveConfig(configurator));
    }

    public static ISignal<T> ToLinkedSignal<T>(this Observable<T> @this,
                                               ReadonlySignalConfigurationDelegate<T?>? configurator = null)
    {
        return CreateSignal(@this, ResolveConfig(configurator));
    }

    internal static IAsyncSignal<T> ToAsyncLinkedSignal<T>(this Observable<T> @this,
                                                           IReadOnlySignal<bool> isExecuting,
                                                           ReadonlySignalConfigurationDelegate<T?>? configurator = null)
    {
        return CreateAsyncSignal(@this, isExecuting, ResolveConfig(configurator));
    }

    internal static IAsyncReadOnlySignal<T> ToAsyncSignal<T>(this Observable<T> @this,
                                                             IReadOnlySignal<bool> isExecuting,
                                                             ReadonlySignalConfigurationDelegate<T?>? configurator = null)
    {
        return CreateAsyncSignal(@this, isExecuting, ResolveConfig(configurator));
    }

    internal static ISignal<T> CreateSignal<T>(Observable<T> observable, ReadonlySignalConfiguration<T?> config)
    {
        return config.SubscriptionStrategy == SubscriptionStrategy.RefCount
            ? new FromObservableSignalRefCounted<T>(observable, config)
            : new FromObservableSignal<T>(observable, config);
    }

    internal static IAsyncSignal<T> CreateAsyncSignal<T>(Observable<T> observable, IReadOnlySignal<bool> isExecuting, ReadonlySignalConfiguration<T?> config)
    {
        return config.SubscriptionStrategy == SubscriptionStrategy.RefCount
            ? new FromObservableAsyncSignalRefCounted<T>(observable, isExecuting, config)
            : new FromObservableAsyncSignal<T>(observable, isExecuting, config);
    }

    public static IReadOnlySignal<TCollection> ToCollectionSignal<TCollection>(this TCollection collection, CollectionChangedSignalConfigurationDelegate? configurator = null)
        where TCollection : INotifyCollectionChanged
    {
        return Signal.FromObservableCollection(collection, configurator);
    }

    internal static ReadonlySignalConfiguration<T> ResolveConfig<T>(ReadonlySignalConfigurationDelegate<T>? configurator)
    {
        var config = ReadonlySignalConfiguration<T>.Default;
        return configurator is null ? config : configurator(config);
    }
}
