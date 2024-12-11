using System.Diagnostics.CodeAnalysis;

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

static class OptionalExtensions
{
    public static bool TryGetValue<T>(this Optional<T> @this, [NotNullWhen(true)] out T? value)
    {
        value = @this.HasValue ? @this.Value : default;
        return @this.HasValue;
    }
}