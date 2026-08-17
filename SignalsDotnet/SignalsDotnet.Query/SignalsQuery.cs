using System.Diagnostics.CodeAnalysis;
using SignalsDotnet.Query.Internals;

namespace SignalsDotnet.Query;

public sealed class SignalsQuery : IEquatable<SignalsQuery>
{
    readonly IReadOnlyList<SelectionField> _fields;

    public SignalsQuery(string query)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        Text = query;
        _fields = SelectionQuery.Parse(query);
    }

    public string Text { get; }

    internal IReadOnlyList<SelectionField> Fields => _fields;

    public static SignalsQuery Parse(string query) => new(query);

    public static bool TryParse(string? query, [NotNullWhen(true)] out SignalsQuery? result)
    {
        if (query is not null)
        {
            try
            {
                result = new SignalsQuery(query);
                return true;
            }
            catch (FormatException)
            {
            }
        }

        result = null;
        return false;
    }

    public static implicit operator SignalsQuery(string query) => new(query);

    public bool Equals(SignalsQuery? other) => other is not null
                                            && (ReferenceEquals(this, other) || _fields.SequenceEqual(other._fields));

    public override bool Equals(object? obj) => Equals(obj as SignalsQuery);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var field in _fields)
            hash.Add(field);

        return hash.ToHashCode();
    }

    public static bool operator ==(SignalsQuery? left, SignalsQuery? right) => left?.Equals(right) ?? right is null;
    public static bool operator !=(SignalsQuery? left, SignalsQuery? right) => !(left == right);

    public override string ToString() => Text;
}
