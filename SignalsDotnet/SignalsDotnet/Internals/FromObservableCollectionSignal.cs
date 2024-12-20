using System.Collections.Specialized;
using System.ComponentModel;
using R3;
using SignalsDotnet.Configuration;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet.Internals;

internal class FromObservableCollectionSignal<T> : Observable<T>, IReadOnlySignal<T> where T : INotifyCollectionChanged
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
        
        Observable<Unit> collectionChanged = _collectionChanged;
        collectionChanged = configuration.CollectionChangedObservableMapper.Invoke(collectionChanged);
        ValuesUnit = collectionChanged.Prepend(Unit.Default);
    }

    void OnCollectionChanged((object? sender, NotifyCollectionChangedEventArgs e) _) => _collectionChanged.OnNext(default);

    protected override IDisposable SubscribeCore(Observer<T> observer) => ValuesUnit.Select(_ => Value)
                                                                                    .Subscribe(observer.OnNext, observer.OnErrorResume, observer.OnCompleted);
    public Observable<Unit> ValuesUnit { get; }

    readonly T _value;
    public T Value => Signal.GetValue(this, in _value);

    object IReadOnlySignal.UntrackedValue => UntrackedValue;
    public T UntrackedValue => _value;
    public event PropertyChangedEventHandler? PropertyChanged;
}