using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace SignalsDotnet.Query.Internals;

static class ProjectionBuilder
{
    internal static Expression BuildProjection(Expression source, IReadOnlyList<SelectionField> fields, JsonSerializerOptions options)
    {
        source = Unwrap(source);

        if (fields.Count == 0)
            return source;

        if (TryGetDictionaryValueType(source.Type, out var keyType, out var valueType))
            return BuildDictionaryProjection(source, keyType, valueType, fields, options);

        if (TryGetEnumerableElementType(source.Type, out var elementType))
            return BuildSequenceProjection(source, elementType, fields, options);

        var properties = GetJsonProperties(source.Type, options);
        var entries = new List<ElementInit>(fields.Count);
        var add = typeof(Dictionary<string, object?>).GetMethod(nameof(Dictionary<string, object?>.Add))!;

        foreach (var field in fields)
        {
            if (!properties.TryGetValue(field.Name, out var property))
                throw new FormatException($"'{source.Type.Name}' has no JSON property named '{field.Name}'.");

            Expression value = Expression.Property(source, property);
            var projected = BuildProjection(value, field.Children, options);

            entries.Add(Expression.ElementInit(add, Expression.Constant(field.Name), Box(projected)));
        }

        Expression dictionary = Expression.ListInit(Expression.New(typeof(Dictionary<string, object?>)), entries);

        return source.Type.IsValueType && Nullable.GetUnderlyingType(source.Type) is null
            ? dictionary
            : NullGuard(source, dictionary);
    }

    static Expression Unwrap(Expression source)
    {
        while (typeof(IReadOnlySignal).IsAssignableFrom(source.Type)
            && source.Type.GetProperty(nameof(IReadOnlySignal<object>.Value)) is { CanRead: true } value)
        {
            source = Expression.Property(source, value);
        }

        return source;
    }

    static Expression BuildDictionaryProjection(Expression source, Type keyType, Type valueType, IReadOnlyList<SelectionField> fields, JsonSerializerOptions options)
    {
        var pairType = typeof(KeyValuePair<,>).MakeGenericType(keyType, valueType);
        var pair = Expression.Parameter(pairType, "pair");

        var key = Expression.Call(Expression.Property(pair, nameof(KeyValuePair<int, int>.Key)), typeof(object).GetMethod(nameof(ToString))!);
        var value = Box(BuildProjection(Expression.Property(pair, nameof(KeyValuePair<int, int>.Value)), fields, options));

        var toDictionary = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                                             .First(m => m.Name == nameof(Enumerable.ToDictionary)
                                                      && m.GetGenericArguments().Length == 3
                                                      && m.GetParameters().Length == 4)
                                             .MakeGenericMethod(pairType, typeof(string), typeof(object));

        var projected = Expression.Call(
            toDictionary,
            source,
            Expression.Lambda(key, pair),
            Expression.Lambda(value, pair),
            Expression.Constant(null, typeof(IEqualityComparer<string>)));

        return NullGuard(source, projected);
    }

    static bool TryGetDictionaryValueType(Type type, out Type keyType, out Type valueType)
    {
        keyType = typeof(object);
        valueType = typeof(object);

        var dictionary = type.GetInterfaces()
                             .Prepend(type)
                             .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
                        ?? type.GetInterfaces()
                               .Prepend(type)
                               .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));

        if (dictionary is null)
            return false;

        var arguments = dictionary.GetGenericArguments();
        keyType = arguments[0];
        valueType = arguments[1];
        return true;
    }

    static Expression BuildSequenceProjection(Expression source, Type elementType, IReadOnlyList<SelectionField> fields, JsonSerializerOptions options)
    {
        var element = Expression.Parameter(elementType, "element");
        var projected = Box(BuildProjection(element, fields, options));

        var select = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                                       .First(m => m.Name == nameof(Enumerable.Select)
                                                && m.GetParameters().Length == 2
                                                && m.GetParameters()[1].ParameterType.GetGenericArguments().Length == 2)
                                       .MakeGenericMethod(elementType, typeof(object));

        var toList = typeof(Enumerable).GetMethod(nameof(Enumerable.ToList))!.MakeGenericMethod(typeof(object));

        var selector = Expression.Lambda(projected, element);
        var sequence = Expression.Call(toList, Expression.Call(select, source, selector));

        return NullGuard(source, sequence);
    }

    static Expression NullGuard(Expression source, Expression whenNotNull) =>
        Expression.Condition(
            Expression.Equal(source, Expression.Constant(null, source.Type)),
            Expression.Constant(null, typeof(object)),
            Box(whenNotNull));

    static Expression Box(Expression expression) =>
        expression.Type == typeof(object) ? expression : Expression.Convert(expression, typeof(object));

    static bool TryGetEnumerableElementType(Type type, out Type elementType)
    {
        elementType = typeof(object);

        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
            return false;

        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        var enumerable = type.GetInterfaces()
                             .Prepend(type)
                             .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        if (enumerable is null)
            return false;

        elementType = enumerable.GetGenericArguments()[0];
        return true;
    }

    static readonly Dictionary<JsonSerializerOptions, JsonSerializerOptions> Resolvable = [];

    static JsonSerializerOptions EnsureResolver(JsonSerializerOptions options)
    {
        if (options.TypeInfoResolver is not null)
            return options;

        lock (Resolvable)
        {
            if (Resolvable.TryGetValue(options, out var resolvable))
                return resolvable;

            resolvable = new JsonSerializerOptions(options) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
            Resolvable[options] = resolvable;
            return resolvable;
        }
    }

    static Dictionary<string, PropertyInfo> GetJsonProperties(Type type, JsonSerializerOptions options)
    {
        var info = EnsureResolver(options).GetTypeInfo(type);
        var properties = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);

        foreach (var property in info.Properties)
            if (property is { Get: not null, AttributeProvider: PropertyInfo clr })
                properties[property.Name] = clr;

        return properties;
    }
}
