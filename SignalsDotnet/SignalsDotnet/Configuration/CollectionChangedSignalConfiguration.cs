using R3;

namespace SignalsDotnet.Configuration;

public delegate CollectionChangedSignalConfiguration CollectionChangedSignalConfigurationDelegate(CollectionChangedSignalConfiguration startConfiguration);

public record CollectionChangedSignalConfiguration(bool SubscribeWeakly, Func<Observable<Unit>, Observable<Unit>> CollectionChangedObservableMapper)
{
    public static CollectionChangedSignalConfiguration Default => new(true, static x => x);
}