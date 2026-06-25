namespace SignalsDotnet.Configuration;

public delegate ReadonlySignalConfiguration<T> ReadonlySignalConfigurationDelegate<T>(ReadonlySignalConfiguration<T> startConfiguration);
public record ReadonlySignalConfiguration<T>(IEqualityComparer<T> Comparer,
                                             bool RaiseOnlyWhenChanged,
                                             bool SubscribeWeakly,
                                             SubscriptionStrategy SubscriptionStrategy = SubscriptionStrategy.Persistent)
{
    public static ReadonlySignalConfiguration<T> Default { get; set; } = new(EqualityComparer<T>.Default, true, false, SubscriptionStrategy.Persistent);
}