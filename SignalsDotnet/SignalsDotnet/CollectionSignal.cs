using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Linq;
using SignalsDotnet.Configuration;

namespace SignalsDotnet;

public class CollectionSignal<T> : IReadOnlySignal<T?> where T : class, INotifyCollectionChanged
{
    readonly CollectionChangedSignalConfigurationDelegate? _collectionChangedConfiguration;
    readonly Signal<IReadOnlySignal<T>> _signal;

    public CollectionSignal(CollectionChangedSignalConfigurationDelegate? collectionChangedConfiguration = null,
                            SignalConfigurationDelegate<IReadOnlySignal<T>>? propertyChangedConfiguration = null)
    {
        _collectionChangedConfiguration = collectionChangedConfiguration;
        _signal = new(propertyChangedConfiguration);

        _signal.PropertyChanged += (_, args) =>
        {
            PropertyChanged?.Invoke(this, args);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UntrackedValue)));
        };
    }

    public T? Value
    {
        get => _signal.Value?.Value;
        set => _signal.Value = value?.ToCollectionSignal(_collectionChangedConfiguration);
    }

    public IObservable<Unit> Changed => this.Select(static _ => Unit.Default);

    public IDisposable Subscribe(IObserver<T?> observer)
    {
        return _signal.Select(static x => x ?? Observable.Empty<T>())
                      .Switch()
                      .Subscribe(observer);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    object? IReadOnlySignal.UntrackedValue => UntrackedValue;
    public T? UntrackedValue => _signal.UntrackedValue?.UntrackedValue;
    public T? UntrackedCollectionChangedValue => _signal.Value?.UntrackedValue;
}