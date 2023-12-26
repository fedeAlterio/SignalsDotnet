using System.Collections.Specialized;
using SignalsDotnet.Configuration;
using SignalsDotnet.Internals;

namespace SignalsDotnet;

public partial class Signal
{
    public static Signal<T> Create<T>(SignalConfigurationDelegate<T>? configurator = null)
    {
        return new Signal<T>(configurator);
    }

    public static Signal<T> Create<T>(T? startValue, SignalConfigurationDelegate<T>? configurator = null)
    {
        return new Signal<T>(startValue, configurator);
    }

    public static IReadOnlySignal<TCollection> FromObservableCollection<TCollection>(TCollection observableCollection, CollectionChangedSignalConfigurationDelegate? configurator = null) where TCollection : INotifyCollectionChanged
    {
        return new FromObservableCollectionSignal<TCollection>(observableCollection, configurator);
    }

    public static CollectionSignal<TCollection> CreateCollectionSignal<TCollection>(CollectionChangedSignalConfigurationDelegate? collectionChangedConfiguration = null,
                                                                                    SignalConfigurationDelegate<IReadOnlySignal<TCollection>>? propertyChangedConfiguration = null) where TCollection : class, INotifyCollectionChanged
    {
        return new CollectionSignal<TCollection>(collectionChangedConfiguration, propertyChangedConfiguration);
    }

    public static IReadOnlySignal<T?> FromObservable<T>(IObservable<T> observable, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return new FromObservableSignal<T>(observable, configuration);
    }
}

public static class SignalFactoryExtensions
{
    public static IReadOnlySignal<T?> ToSignal<T>(this IObservable<T> @this,
                                                  ReadonlySignalConfigurationDelegate<T?>? configurator = null)
    {
        return new FromObservableSignal<T>(@this, configurator);
    }

    public static IReadOnlySignal<TCollection> ToCollectionSignal<TCollection>(this TCollection collection, CollectionChangedSignalConfigurationDelegate? configurator = null)
        where TCollection : INotifyCollectionChanged
    {
        return Signal.FromObservableCollection(collection, configurator);
    }
}