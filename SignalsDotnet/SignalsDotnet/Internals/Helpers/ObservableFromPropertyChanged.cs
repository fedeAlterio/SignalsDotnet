using System.ComponentModel;
using System.Reactive.Linq;

namespace SignalsDotnet.Internals.Helpers;

internal static class ObservableFromPropertyChanged
{
    public static IObservable<T> OnPropertyChanged<T>(this INotifyPropertyChanged @this,
                                                      string propertyName,
                                                      Func<T> getter)
    {
        return Observable.FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(x => @this.PropertyChanged += x,
                                                                                                  x => @this.PropertyChanged -= x)
                         .Where(x => x.EventArgs.PropertyName == propertyName)
                         .Select(_ => getter())
                         .StartWith(getter());
    }
}