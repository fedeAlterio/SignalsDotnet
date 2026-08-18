using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            Expression value;

            if (field.IsCall)
                value = BuildCall(source, field, options);
            else if (properties.TryGetValue(field.Name, out var property))
                value = Expression.Property(source, property);
            else if (TryFindMethod(source.Type, field, options, out var method))
                value = BuildCall(source, field, method, options);
            else
                throw new FormatException(NotFound(source.Type, field, options));

            var projected = BuildProjection(value, field.Children, options);

            entries.Add(Expression.ElementInit(add, Expression.Constant(field.Key), Box(projected)));
        }

        Expression dictionary = Expression.ListInit(Expression.New(typeof(Dictionary<string, object?>)), entries);

        return source.Type.IsValueType && Nullable.GetUnderlyingType(source.Type) is null
            ? dictionary
            : NullGuard(source, dictionary);
    }

    static Expression BuildCall(Expression source, SelectionField field, JsonSerializerOptions options)
    {
        if (!TryFindMethod(source.Type, field, options, out var method))
            throw new FormatException(NotFound(source.Type, field, options));

        return BuildCall(source, field, method, options);
    }

    static string NotFound(Type type, SelectionField field, JsonSerializerOptions options)
    {
        var hidden = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Any(x => IsQueryable(x) && NameMatches(field.Name, x.Name, options));

        return hidden
            ? $"'{type.Name}.{field.Name}' is not queryable. Annotate it with [{nameof(SignalQueryableAttribute)}] to expose it."
            : $"'{type.Name}' has no queryable property or method named '{field.Name}'.";
    }

    static Expression BuildCall(Expression source, SelectionField field, MethodInfo method, JsonSerializerOptions options)
    {
        var parameters = method.GetParameters();
        var arguments = new Expression[parameters.Length];

        var unknown = field.ArgumentsOrEmpty.FirstOrDefault(x => !parameters.Any(p => NameMatches(x.Name, p.Name, options)));

        if (unknown is not null)
            throw new FormatException($"'{field.Name}' has no argument named '{unknown.Name}'.");

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var supplied = field.ArgumentsOrEmpty.FirstOrDefault(x => NameMatches(x.Name, parameter.Name, options));

            if (supplied is null)
            {
                if (!parameter.HasDefaultValue)
                    throw new FormatException($"Argument '{parameter.Name}' of '{field.Name}' is required.");

                arguments[i] = Expression.Constant(parameter.DefaultValue, parameter.ParameterType);
                continue;
            }

            arguments[i] = Expression.Constant(ConvertArgument(supplied, parameter, field), parameter.ParameterType);
        }

        return Expression.Call(source, method, arguments);
    }

    static object? ConvertArgument(SelectionArgument argument, ParameterInfo parameter, SelectionField field)
    {
        var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

        if (argument.Value is null)
        {
            if (parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) is null)
                throw new FormatException($"Argument '{argument.Name}' of '{field.Name}' cannot be null.");

            return null;
        }

        if (type.IsInstanceOfType(argument.Value))
            return argument.Value;

        try
        {
            if (type.IsEnum)
                return argument.Value is string name
                    ? Enum.Parse(type, name, ignoreCase: true)
                    : Enum.ToObject(type, argument.Value);

            if (type == typeof(Guid) && argument.Value is string guid)
                return Guid.Parse(guid);

            if (type == typeof(TimeSpan) && argument.Value is string span)
                return TimeSpan.Parse(span, CultureInfo.InvariantCulture);

            if (type == typeof(DateTime) && argument.Value is string dateTime)
                return DateTime.Parse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            if (type == typeof(DateTimeOffset) && argument.Value is string dateTimeOffset)
                return DateTimeOffset.Parse(dateTimeOffset, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            return Convert.ChangeType(argument.Value, type, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            throw new FormatException($"Argument '{argument.Name}' of '{field.Name}' is not a valid {type.Name}.");
        }
    }

    static bool TryFindMethod(Type type, SelectionField field, JsonSerializerOptions options, [NotNullWhen(true)] out MethodInfo? method)
    {
        var candidates = GetQueryableMethods(type).Where(x => NameMatches(field.Name, x.Name, options)).ToList();

        if (candidates.Count > 1)
        {
            var matching = candidates.Where(x => Binds(x, field, options)).ToList();

            if (matching.Count > 1)
                throw new FormatException($"'{field.Name}' is ambiguous between {matching.Count} overloads.");

            candidates = matching;
        }

        method = candidates.FirstOrDefault();
        return method is not null;
    }

    static bool Binds(MethodInfo method, SelectionField field, JsonSerializerOptions options)
    {
        var parameters = method.GetParameters();

        return parameters.All(p => p.HasDefaultValue || field.ArgumentsOrEmpty.Any(a => NameMatches(a.Name, p.Name, options)))
            && field.ArgumentsOrEmpty.All(a => parameters.Any(p => NameMatches(a.Name, p.Name, options)));
    }

    static bool NameMatches(string queried, string? declared, JsonSerializerOptions options) =>
        declared is not null
     && (string.Equals(queried, declared, StringComparison.Ordinal)
      || string.Equals(queried, options.PropertyNamingPolicy?.ConvertName(declared), StringComparison.Ordinal));

    internal static IEnumerable<MethodInfo> GetQueryableMethods(Type type)
    {
        var all = type.GetCustomAttribute<SignalQueryableAttribute>(inherit: true) is not null;

        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                   .Where(x => IsQueryable(x)
                            && (all || x.GetCustomAttribute<SignalQueryableAttribute>(inherit: true) is not null));
    }

    static bool IsQueryable(MethodInfo method) => !method.IsSpecialName
                                               && !method.IsGenericMethodDefinition
                                               && method.DeclaringType != typeof(object)
                                               && method.ReturnType != typeof(void)
                                               && !method.GetParameters().Any(p => p.IsOut || p.ParameterType.IsByRef)
                                               && !typeof(Task).IsAssignableFrom(method.ReturnType)
                                               && !typeof(ValueTask).IsAssignableFrom(method.ReturnType)
                                               && !(method.ReturnType.IsGenericType
                                                 && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>));

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
