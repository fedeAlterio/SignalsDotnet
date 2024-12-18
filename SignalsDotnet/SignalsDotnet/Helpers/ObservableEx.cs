using System.Reactive.Linq;

namespace SignalsDotnet.Helpers;

public static class ObservableEx
{
    public static IObservable<T> DisconnectWhen<T>(this IObservable<T> @this, IObservable<bool> isDisconnected)
    {
        return isDisconnected.StartWith(true)
                             .DistinctUntilChanged()
                             .Replay(1)
                             .AutoConnect(0)
                             .Select(x => x switch
                             {
                                 false => @this,
                                 true => Observable.Empty<T>()
                             })
                             .Switch()
                             .Publish()
                             .RefCount();
    }
}
