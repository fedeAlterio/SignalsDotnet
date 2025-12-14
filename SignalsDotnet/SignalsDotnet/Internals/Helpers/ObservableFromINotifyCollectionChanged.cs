using System.Collections.Specialized;
using R3;

namespace SignalsDotnet.Internals.Helpers;

internal static class ObservableFromINotifyCollectionChanged
{
    public static Observable<(object? sender, NotifyCollectionChangedEventArgs e)> OnCollectionChanged(this INotifyCollectionChanged collection)
    {
        return new CollectionChangedObservable(collection);
    }

    sealed class CollectionChangedObservable(INotifyCollectionChanged notifyCollectionChanged) : Observable<(object? sender, NotifyCollectionChangedEventArgs e)>
    {
        readonly INotifyCollectionChanged _notifyCollectionChanged = notifyCollectionChanged;

        protected override IDisposable SubscribeCore(Observer<(object? sender, NotifyCollectionChangedEventArgs e)> observer)
        {
            return new Subscription(observer, this);
        }


        struct Subscription : IDisposable
        {
            readonly Observer<(object? sender, NotifyCollectionChangedEventArgs e)> _observer;
            readonly CollectionChangedObservable _observable;

            public Subscription(Observer<(object? sender, NotifyCollectionChangedEventArgs e)> observer, CollectionChangedObservable observable)
            {
                _observer = observer;
                _observable = observable;
                _observable._notifyCollectionChanged.CollectionChanged += OnCollectionChanged;
            }

            void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            {
                _observer.OnNext((sender, e));
            }

            public void Dispose()
            {
                _observable._notifyCollectionChanged.CollectionChanged -= OnCollectionChanged;
            }
        }
    }
}