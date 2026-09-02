using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using SignalsDotnet.Query.Internals;

namespace SignalsDotnet.Query;

public class SignalComputedQuery : IEquatable<SignalComputedQuery>
{
    readonly IReadOnlyList<SelectionField> _fields;

    private protected SignalComputedQuery(IReadOnlyList<SelectionField> fields, string text)
    {
        _fields = fields;
        Text = text;
    }

    public SignalComputedQuery(string query)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        Text = query;
        _fields = SelectionQuery.Parse(query);
    }

    SignalComputedQuery(IReadOnlyList<SelectionField> fields)
    {
        _fields = fields;
        Text = QueryBuilder.Write(fields);
    }

    public string Text { get; }

    internal IReadOnlyList<SelectionField> Fields => _fields;

    public static SignalComputedQuery Parse(string query) => new(query);

    public static SignalComputedQuery<TSource, object?> Create<TSource>(Expression<Func<TSource, object?>> selector, NamingConventionOptions? naming = null) =>
        Build<TSource, object?>(selector, naming);

    public static SignalComputedQuery<TSource, TResult> Create<TSource, TResult>(Expression<Func<TSource, TResult>> selector, NamingConventionOptions? naming = null) =>
        Build<TSource, TResult>(selector, naming);

    static SignalComputedQuery<TSource, TResult> Build<TSource, TResult>(Expression<Func<TSource, TResult>> selector, NamingConventionOptions? naming)
    {
        if (selector is null)
            throw new ArgumentNullException(nameof(selector));

        var fields = QueryBuilder.Build(selector, (naming ?? NamingConventionOptions.Default).Convention);

        return new SignalComputedQuery<TSource, TResult>(fields, QueryBuilder.Write(fields), selector);
    }

    public static SignalComputedQuery Create(LambdaExpression selector, NamingConventionOptions? naming = null) =>
        FromLambda(selector, naming);

    static SignalComputedQuery FromLambda(LambdaExpression selector, NamingConventionOptions? naming)
    {
        if (selector is null)
            throw new ArgumentNullException(nameof(selector));

        return new SignalComputedQuery(QueryBuilder.Build(selector, (naming ?? NamingConventionOptions.Default).Convention));
    }

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
