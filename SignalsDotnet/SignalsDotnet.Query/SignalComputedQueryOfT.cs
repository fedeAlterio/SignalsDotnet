using System.Linq.Expressions;
using SignalsDotnet.Query.Internals;

namespace SignalsDotnet.Query;

public sealed class SignalComputedQuery<TSource, TResult> : SignalComputedQuery
{
    internal SignalComputedQuery(IReadOnlyList<SelectionField> fields, string text, Expression<Func<TSource, TResult>> selector)
        : base(fields, text) => Selector = selector;

    public Expression<Func<TSource, TResult>> Selector { get; }
}
