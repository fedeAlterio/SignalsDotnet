using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SignalsDotnet.Helpers;

public readonly struct Optional<T>
{
    readonly T? _value;
    public Optional() => (_value, HasValue) = (default, false);
    public Optional(T value) => (_value, HasValue) = (value, true);
    public static Optional<T> Empty => new();
    public bool HasValue { get; }
    public T? Value => HasValue ? _value : throw new InvalidOperationException("Impossible retrieve a value for an empty optional");
}

public static class OptionalExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetValue<T>(this Optional<T> @this, [NotNullWhen(true)] out T? value)
    {
        var hasValue = @this.HasValue;
        value = hasValue ? @this.Value : default;
        return hasValue;
    }
}