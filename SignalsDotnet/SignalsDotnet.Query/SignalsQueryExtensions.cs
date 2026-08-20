using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using R3;
using SignalsDotnet.Query.Internals;

namespace SignalsDotnet.Query;

public static class SignalsQueryExtensions
{
    public static JsonSerializerOptions DefaultJsonOptions { get; set; } = new(JsonSerializerDefaults.Web);

    public static Expression<Func<T, object?>> ToQuerySelectorExpression<T>(this SignalComputedQuery query, JsonSerializerOptions? options = null)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        var parameter = Expression.Parameter(typeof(T), "source");
        var body = ProjectionBuilder.BuildProjection(parameter, query.Fields, options ?? DefaultJsonOptions);

        return Expression.Lambda<Func<T, object?>>(Expression.Convert(body, typeof(object)), parameter);
    }

    public static Func<T, object?> ToQuerySelector<T>(this SignalComputedQuery query, JsonSerializerOptions? options = null) =>
        query.ToQuerySelectorExpression<T>(options).Compile();

    public static bool IsAsync<T>(this SignalComputedQuery query, JsonSerializerOptions? options = null)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        return ProjectionBuilder.IsAsyncProjection(typeof(T), query.Fields, options ?? DefaultJsonOptions);
    }

    public static Expression<Func<T, ValueTask<object?>>> ToAsyncQuerySelectorExpression<T>(this SignalComputedQuery query, JsonSerializerOptions? options = null)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        var parameter = Expression.Parameter(typeof(T), "source");
        var body = ProjectionBuilder.BuildAsyncProjection(parameter, query.Fields, options ?? DefaultJsonOptions);

        return Expression.Lambda<Func<T, ValueTask<object?>>>(body, parameter);
    }

    public static Func<T, ValueTask<object?>> ToAsyncQuerySelector<T>(this SignalComputedQuery query, JsonSerializerOptions? options = null) =>
        query.ToAsyncQuerySelectorExpression<T>(options).Compile();

    public static IEnumerable<MethodInfo> GetQueryableMethods(Type type)
    {
        if (type is null)
            throw new ArgumentNullException(nameof(type));

        return ProjectionBuilder.GetQueryableMethods(type);
    }

    public static Observable<object?> ComputedObservable<T>(this SignalComputedQuery query, T source, JsonSerializerOptions? options = null)
    {
        if (query.IsAsync<T>(options))
        {
            var asyncSelector = query.ToAsyncQuerySelector<T>(options);

            return Signal.AsyncComputedObservable(_ => asyncSelector(source));
        }

        var selector = query.ToQuerySelector<T>(options);

        return Signal.ComputedObservable(() => selector(source));
    }
}
