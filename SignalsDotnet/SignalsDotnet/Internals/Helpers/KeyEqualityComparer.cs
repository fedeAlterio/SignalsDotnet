namespace SignalsDotnet.Internals.Helpers;

internal sealed class KeyEqualityComparer<T, TDestination>(Func<T?, TDestination> keyExtractor) : IEqualityComparer<T>
    where TDestination : notnull
{
    readonly EqualityComparer<TDestination> _equalityComparer = EqualityComparer<TDestination>.Default;

    public bool Equals(T? x, T? y)
    {
        return _equalityComparer.Equals(keyExtractor(x), keyExtractor(y));
    }

    public int GetHashCode(T? obj)
    {
        return _equalityComparer.GetHashCode(keyExtractor(obj));
    }
}