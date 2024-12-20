using System.Collections.Specialized;
using System.ComponentModel;
using R3;
using SignalsDotnet.Configuration;

namespace SignalsDotnet;

public class CollectionSignal<T> : IReadOnlySignal<T?> where T : class, INotifyCollectionChanged
{
    readonly CollectionChangedSignalConfigurationDelegate? _collectionChangedConfiguration;
    readonly Signal<IReadOnlySignal<T>?> _signal;

    public CollectionSignal(CollectionChangedSignalConfigurationDelegate? collectionChangedConfiguration = null,
                            SignalConfigurationDelegate<IReadOnlySignal<T>?>? propertyChangedConfiguration = null)
    {
        _collectionChangedConfiguration = collectionChangedConfiguration;
        _signal = new(propertyChangedConfiguration);

        _signal.PropertyChanged += (_, args) =>
        {
            PropertyChanged?.Invoke(this, args);
        };
    }

    public T? Value
    {
        get => _signal.Value?.Value;
        set => _signal.Value = value?.ToCollectionSignal(_collectionChangedConfiguration)!;
    }

    public Observable<T?> Values => _signal.Values
                                           .Select(static x => x?.Values ?? Observable.Return<T?>(null)!)
                                           .Switch()!;
    
    public Observable<T?> FutureValues => Values.Skip(1);

 
    public event PropertyChangedEventHandler? PropertyChanged;
    object? IReadOnlySignal.UntrackedValue => UntrackedValue;
    public T? UntrackedValue => _signal.UntrackedValue?.UntrackedValue;
    public T? UntrackedCollectionChangedValue => _signal.Value?.UntrackedValue;

    Observable<Unit> IReadOnlySignal.Values => _signal.Values
                                                      .Select(static x => ((IReadOnlySignal?)x)?.Values ?? Observable.Return<Unit>(Unit.Default))
                                                      .Switch();
}