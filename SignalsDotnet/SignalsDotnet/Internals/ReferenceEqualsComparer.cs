using System.Runtime.CompilerServices;

namespace SignalsDotnet.Internals;

internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
{
    internal static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

    private ReferenceEqualityComparer() { }

    public bool Equals(T x, T y)
    {
        return object.ReferenceEquals(x, y);
    }

    public int GetHashCode(T obj)
    {
        return RuntimeHelpers.GetHashCode(obj);
    }
}