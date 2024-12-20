using R3;

namespace SignalsDotnet.Helpers;

public static class ObservableEx
{
    public static Observable<T> DisconnectWhen<T>(this Observable<T> @this, Observable<bool> isDisconnected)
    {
        return isDisconnected.Select(x => x switch
                             {
                                 false => @this,
                                 true => Observable.Empty<T>()
                             })
                             .Switch()
                             .Publish()
                             .RefCount();
    }
}
