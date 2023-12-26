namespace SignalsDotnet.Internals.Helpers;

class KeyEqualityComparer<T, TDestination> : IEqualityComparer<T> where TDestination : notnull
{
    readonly Func<T?, TDestination> _keyExtractor;
    readonly EqualityComparer<TDestination> _equalityComparer = EqualityComparer<TDestination>.Default;

    public KeyEqualityComparer(Func<T?, TDestination> keyExtractor)
    {
        _keyExtractor = keyExtractor;
    }

    public bool Equals(T? x, T? y)
    {
        return _equalityComparer.Equals(_keyExtractor(x), _keyExtractor(y));
    }

    public int GetHashCode(T? obj)
    {
        return _equalityComparer.GetHashCode(_keyExtractor(obj));
    }
}