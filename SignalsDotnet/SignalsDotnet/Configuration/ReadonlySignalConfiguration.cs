namespace SignalsDotnet.Configuration;

public delegate ReadonlySignalConfiguration<T> ReadonlySignalConfigurationDelegate<T>(ReadonlySignalConfiguration<T> startConfiguration);
public record ReadonlySignalConfiguration<T>(IEqualityComparer<T> Comparer,
                                             bool RaiseOnlyWhenChanged,
                                             bool SubscribeWeakly)
{
    public static ReadonlySignalConfiguration<T> Default { get; } = new(EqualityComparer<T>.Default, true, false);
}