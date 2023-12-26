using System.Reactive;
using System.Reactive.Concurrency;

namespace SignalsDotnet.Configuration;

public delegate CollectionChangedSignalConfiguration CollectionChangedSignalConfigurationDelegate(CollectionChangedSignalConfiguration startConfiguration);

public record CollectionChangedSignalConfiguration(bool SubscribeWeakly, Func<IObservable<Unit>, IObservable<Unit>> CollectionChangedObservableMapper)
{
    public static CollectionChangedSignalConfiguration Default => new(true, static x => x);
}

public static class CollectionChangedSignalConfigurationExtensions
{
    public static CollectionChangedSignalConfiguration ThrottleOneCycle(this CollectionChangedSignalConfiguration @this, IScheduler scheduler)
    {
        return @this with { CollectionChangedObservableMapper = x => x.ThrottleOneCycle(scheduler) };
    }
}