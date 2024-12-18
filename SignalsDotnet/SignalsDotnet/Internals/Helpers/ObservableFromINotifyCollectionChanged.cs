using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Linq;

namespace SignalsDotnet.Internals.Helpers;

internal static class ObservableFromINotifyCollectionChanged
{
    public static IObservable<EventPattern<NotifyCollectionChangedEventArgs>> OnCollectionChanged(this INotifyCollectionChanged collection)
    {
        return Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(x => collection.CollectionChanged += x, x => collection.CollectionChanged -= x);
    }
}