using R3;

namespace SignalsDotnet.Helpers;

public static class ObservableEx
{
    public static Observable<T> DisconnectWhen<T>(this Observable<T> @this, Observable<bool> isDisconnected)
    {
        throw new InvalidOperationException();
        //return isDisconnected.Prepend(true)
        //                     .DistinctUntilChanged()
        //                     .Replay(1)
        //                     .AutoConnect(0)
        //                     .Select(x => x switch
        //                     {
        //                         false => @this,
        //                         true => Observable.Empty<T>()
        //                     })
        //                     .Switch()
        //                     .Publish()
        //                     .RefCount();
    }
}
