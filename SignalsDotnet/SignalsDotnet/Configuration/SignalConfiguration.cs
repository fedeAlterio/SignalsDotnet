using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet.Configuration;

public delegate SignalConfiguration<T> SignalConfigurationDelegate<T>(SignalConfiguration<T> startConfiguration);

public record SignalConfiguration<T>(IEqualityComparer<T?> Comparer, bool RaiseOnlyWhenChanged)
{
    public static SignalConfiguration<T> Default { get; } = new(EqualityComparer<T?>.Default, true);
}

public static class SignalConfigurationExtensions
{
    public static SignalConfiguration<T> ForEqualityCheck<T, TDest>(this SignalConfiguration<T> @this,
                                                                    Func<T?, TDest> equalitySelector)
        where TDest : notnull
    {
        return @this with { Comparer = new KeyEqualityComparer<T?, TDest>(equalitySelector) };
    }

    public static ReadonlySignalConfiguration<T> ForEqualityCheck<T, TDest>(this ReadonlySignalConfiguration<T> @this,
                                                                            Func<T?, TDest> equalitySelector)
        where TDest : notnull
    {
        return @this with { Comparer = new KeyEqualityComparer<T?, TDest>(equalitySelector) };
    }
}