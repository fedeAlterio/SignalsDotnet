using System.Diagnostics.CodeAnalysis;
using SignalsDotnet.Query.Internals;

namespace SignalsDotnet.Query;

public sealed class SignalComputedQuery : IEquatable<SignalComputedQuery>
{
    readonly IReadOnlyList<SelectionField> _fields;

    public SignalComputedQuery(string query)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        Text = query;
        _fields = SelectionQuery.Parse(query);
    }

    public string Text { get; }

    internal IReadOnlyList<SelectionField> Fields => _fields;

    public static SignalComputedQuery Parse(string query) => new(query);

    public static bool TryParse(string? query, [NotNullWhen(true)] out SignalComputedQuery? result)
    {
        if (query is not null)
        {
            try
            {
                result = new SignalComputedQuery(query);
                return true;
            }
            catch (FormatException)
            {
            }
        }

        result = null;
        return false;
    }

    public static implicit operator SignalComputedQuery(string query) => new(query);

    public bool Equals(SignalComputedQuery? other) => other is not null
                                            && (ReferenceEquals(this, other) || _fields.SequenceEqual(other._fields));

    public override bool Equals(object? obj) => Equals(obj as SignalComputedQuery);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var field in _fields)
            hash.Add(field);

        return hash.ToHashCode();
    }

    public static bool operator ==(SignalComputedQuery? left, SignalComputedQuery? right) => left?.Equals(right) ?? right is null;
    public static bool operator !=(SignalComputedQuery? left, SignalComputedQuery? right) => !(left == right);

    public override string ToString() => Text;
}
