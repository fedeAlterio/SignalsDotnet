using System.Collections.Specialized;
using System.ComponentModel;
using R3;
using SignalsDotnet.Configuration;

namespace SignalsDotnet;

public class CollectionSignal<T> : Observable<T>, IReadOnlySignal<T?> where T : class, INotifyCollectionChanged
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

    public Observable<Unit> ValuesUnit => _signal.Select(static x => x?.ValuesUnit ?? Observable.Return(Unit.Default))
                                                 .Switch();

    protected override IDisposable SubscribeCore(Observer<T> observer)
    {
        return _signal.Select(static x => x?.ValuesUnit ?? Observable.Return(Unit.Default))
                      .Switch()
                      .Select(_ => Value)
                      .Subscribe(observer.OnNext!, observer.OnErrorResume, observer.OnCompleted);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    object? IReadOnlySignal.UntrackedValue => UntrackedValue;
    public T? UntrackedValue => _signal.UntrackedValue?.UntrackedValue;
    public T? UntrackedCollectionChangedValue => _signal.Value?.UntrackedValue;
}