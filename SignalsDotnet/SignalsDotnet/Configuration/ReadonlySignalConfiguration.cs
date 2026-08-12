namespace SignalsDotnet.Configuration;

public delegate ReadonlySignalConfiguration<T> ReadonlySignalConfigurationDelegate<T>(ReadonlySignalConfiguration<T> startConfiguration);

public record ReadonlySignalConfiguration(
    bool RaiseOnlyWhenChanged,
    bool SubscribeWeakly,
    SubscriptionStrategy SubscriptionStrategy = SubscriptionStrategy.Persistent)
{
    internal static int Version;

    public static ReadonlySignalConfiguration Default
    {
        get => field;
        set
        {
            Version++;
            field = value;
        }
    } = new(true, false);
}
public record ReadonlySignalConfiguration<T>(IEqualityComparer<T> Comparer,
                                             bool RaiseOnlyWhenChanged,
                                             bool SubscribeWeakly,
                                             SubscriptionStrategy SubscriptionStrategy = SubscriptionStrategy.RefCount)
{
    private static int _version = -1;
    public static ReadonlySignalConfiguration<T> Default
    {
        get
        {
            var currentVersion = ReadonlySignalConfiguration.Version;
            if (_version == currentVersion) return field!;

            _version = currentVersion;
            var defaultConfig = ReadonlySignalConfiguration.Default;
            var defaultValue = new ReadonlySignalConfiguration<T>(EqualityComparer<T>.Default,
                defaultConfig.RaiseOnlyWhenChanged, defaultConfig.SubscribeWeakly, defaultConfig.SubscriptionStrategy);
            field = defaultValue;

            return defaultValue;
        }
    }
}