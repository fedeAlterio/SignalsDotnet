using System.Collections.Specialized;
using System.ComponentModel;
using R3;
using SignalsDotnet.Configuration;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet.Internals;

internal class FromObservableCollectionSignal<T> : IReadOnlySignal<T> where T : INotifyCollectionChanged
{
    readonly Subject<Unit> _collectionChanged = new();
    
    public FromObservableCollectionSignal(T collection, CollectionChangedSignalConfigurationDelegate? configurator = null)
    {
        _value = collection ?? throw new ArgumentNullException(nameof(collection));
        var configuration = CollectionChangedSignalConfiguration.Default;
        if (configurator is not null)
        {
            configuration = configurator(configuration);
        }

        var observable = collection.OnCollectionChanged();
        if (configuration.SubscribeWeakly)
        {
            observable.SubscribeWeakly(OnCollectionChanged);
        }
        else
        {
            observable.Subscribe(OnCollectionChanged);
        }
    }

    void OnCollectionChanged((object? sender, NotifyCollectionChangedEventArgs e) _) => _collectionChanged.OnNext(default);

    public Observable<T> Values => _collectionChanged.Select(_ => Value)
                                                    .Prepend(() => Value);

    public Observable<T> FutureValues => _collectionChanged.Select(_ => Value);

    readonly T _value;
    public T Value => Signal.GetValue(this, in _value);

    object IReadOnlySignal.UntrackedValue => UntrackedValue;
    public T UntrackedValue => _value;
    public event PropertyChangedEventHandler? PropertyChanged;

    Observable<Unit> IReadOnlySignal.Values => _collectionChanged.Prepend(Unit.Default);
    Observable<Unit> IReadOnlySignal.FutureValues => _collectionChanged;
}