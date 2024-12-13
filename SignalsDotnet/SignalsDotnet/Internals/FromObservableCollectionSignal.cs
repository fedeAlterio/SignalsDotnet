using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using SignalsDotnet.Configuration;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet.Internals;

internal class FromObservableCollectionSignal<T> : Signal, IReadOnlySignal<T> where T : INotifyCollectionChanged
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
        
        IObservable<Unit> collectionChanged = _collectionChanged;
        collectionChanged = configuration.CollectionChangedObservableMapper.Invoke(collectionChanged);
        Changed = collectionChanged.StartWith(Unit.Default);
    }

    void OnCollectionChanged(EventPattern<NotifyCollectionChangedEventArgs> _) => _collectionChanged.OnNext(default);

    public IDisposable Subscribe(IObserver<T> observer) => Changed.Select(_ => Value)
                                                                  .Subscribe(observer);
    public IObservable<Unit> Changed { get; }

    readonly T _value;
    public T Value => GetValue(this, in _value);
    object IReadOnlySignal.UntrackedValue => UntrackedValue;
    public T UntrackedValue => _value;
}