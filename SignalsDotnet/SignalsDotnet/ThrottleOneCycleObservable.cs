using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace SignalsDotnet;

public static class ThrottleOneCycleObservable
{
    public static IObservable<T> ThrottleOneCycle<T>(this IObservable<T> observable, IScheduler scheduler)
    {
        if (observable is null)
            throw new ArgumentNullException(nameof(observable));

        return observable.Select(x => Observable.Return(x, scheduler))
                         .Switch();
    }
}