using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using R3;
using SignalsDotnet.Query.Internals;

namespace SignalsDotnet.Query;

public static class SignalsQueryExtensions
{
    public static JsonSerializerOptions DefaultJsonOptions { get; set; } = new(JsonSerializerDefaults.Web);

    public static Expression<Func<T, object?>> ToQuerySelectorExpression<T>(this SignalsQuery query, JsonSerializerOptions? options = null)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        var parameter = Expression.Parameter(typeof(T), "source");
        var body = ProjectionBuilder.BuildProjection(parameter, query.Fields, options ?? DefaultJsonOptions);

        return Expression.Lambda<Func<T, object?>>(Expression.Convert(body, typeof(object)), parameter);
    }

    public static Func<T, object?> ToQuerySelector<T>(this SignalsQuery query, JsonSerializerOptions? options = null) =>
        query.ToQuerySelectorExpression<T>(options).Compile();

    public static IEnumerable<MethodInfo> GetQueryableMethods(Type type)
    {
        if (type is null)
            throw new ArgumentNullException(nameof(type));

        return ProjectionBuilder.GetQueryableMethods(type);
    }

    public static Observable<object?> ComputedObservable<T>(this SignalsQuery query, T source, JsonSerializerOptions? options = null)
    {
        var selector = query.ToQuerySelector<T>(options);

        return Signal.ComputedObservable(() => selector(source));
    }
}
