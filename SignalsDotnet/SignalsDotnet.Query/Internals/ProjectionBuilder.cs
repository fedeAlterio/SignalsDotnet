using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace SignalsDotnet.Query.Internals;

static class ProjectionBuilder
{
    internal static Expression BuildProjection(Expression source, IReadOnlyList<SelectionField> fields, INamingConvention naming)
    {
        var node = Build(source, fields, naming);

        if (node.IsAsync)
            throw new FormatException("The query awaits an asynchronous member and cannot be projected synchronously.");

        return node.Expression;
    }

    internal static bool IsAsyncProjection(Type type, IReadOnlyList<SelectionField> fields, INamingConvention naming) =>
        Build(Expression.Parameter(type, "source"), fields, naming).IsAsync;

    internal static Expression BuildAsyncProjection(Expression source, IReadOnlyList<SelectionField> fields, INamingConvention naming)
    {
        var node = Build(source, fields, naming);

        return node.IsAsync
            ? Cast(node.Expression, typeof(ValueTask<object?>))
            : Expression.Call(ProjectionAsync.FromResultMethod.MakeGenericMethod(typeof(object)), Box(node.Expression));
    }

    readonly struct Node(Expression expression, bool isAsync)
    {
        public Expression Expression { get; } = expression;
        public bool IsAsync { get; } = isAsync;

        public Type ValueType => IsAsync ? Expression.Type.GetGenericArguments()[0] : Expression.Type;

        public static Node Sync(Expression expression) => new(expression, false);
        public static Node Async(Expression expression) => new(expression, true);
    }

    static Node Build(Expression source, IReadOnlyList<SelectionField> fields, INamingConvention naming) =>
        Build(Node.Sync(source), fields, naming);

    static Node Build(Node source, IReadOnlyList<SelectionField> fields, INamingConvention naming)
    {
        if (source.IsAsync)
        {
            var awaited = Expression.Parameter(source.ValueType, "awaited");
            var inner = Build(Node.Sync(awaited), fields, naming);

            return Node.Async(Continue(source.Expression, inner, awaited));
        }

        var value = Unwrap(source.Expression);

        if (fields.Count == 0)
            return Node.Sync(value);

        if (TryGetDictionaryValueType(value.Type, out var keyType, out var valueType))
            return BuildDictionaryProjection(value, keyType, valueType, fields, naming);

        if (TryGetEnumerableElementType(value.Type, out var elementType))
            return BuildSequenceProjection(value, elementType, fields, naming);

        var children = new List<(string Key, Node Value)>(fields.Count);

        foreach (var field in fields)
        {
            Node member;

            if (field.IsCall)
                member = BuildCall(value, field, naming);
            else if (TryFindProperty(value.Type, field.Name, naming, out var property))
                member = Node.Sync(Expression.Property(value, property));
            else if (TryFindMethod(value.Type, field, naming, out var method))
                member = BuildCall(value, field, method, naming);
            else
                throw new FormatException(NotFound(value.Type, field, naming));

            children.Add((field.Key, Build(member, field.Children, naming)));
        }

        var nullable = !value.Type.IsValueType || Nullable.GetUnderlyingType(value.Type) is not null;

        if (children.All(x => !x.Value.IsAsync))
        {
            var add = typeof(Dictionary<string, object?>).GetMethod(nameof(Dictionary<string, object?>.Add))!;
            var entries = children.Select(x => Expression.ElementInit(add, Expression.Constant(x.Key), Box(x.Value.Expression)));

            Expression dictionary = Expression.ListInit(Expression.New(typeof(Dictionary<string, object?>)), entries);

            return Node.Sync(nullable ? NullGuard(value, dictionary) : dictionary);
        }

        var keys = Expression.NewArrayInit(typeof(string), children.Select(x => (Expression)Expression.Constant(x.Key)));
        var values = Expression.NewArrayInit(typeof(ValueTask<object?>), children.Select(x => AsAsyncObject(x.Value)));

        Expression combined = Expression.Call(ProjectionAsync.WhenAllMethod, keys, values);

        return Node.Async(nullable ? AsyncNullGuard(value, combined) : combined);
    }

    static Expression Continue(Expression source, Node inner, ParameterExpression awaited)
    {
        var sourceType = source.Type.GetGenericArguments()[0];

        if (inner.IsAsync)
            return Expression.Call(ProjectionAsync.BindMethod.MakeGenericMethod(sourceType, inner.ValueType),
                                   source,
                                   Expression.Lambda(typeof(Func<,>).MakeGenericType(sourceType, inner.Expression.Type), inner.Expression, awaited));

        return Expression.Call(ProjectionAsync.MapMethod.MakeGenericMethod(sourceType, inner.ValueType),
                               source,
                               Expression.Lambda(typeof(Func<,>).MakeGenericType(sourceType, inner.ValueType), inner.Expression, awaited));
    }

    static Expression AsAsyncObject(Node node)
    {
        if (!node.IsAsync)
            return Expression.Call(ProjectionAsync.FromResultMethod.MakeGenericMethod(typeof(object)), Box(node.Expression));

        return node.ValueType == typeof(object)
            ? node.Expression
            : Expression.Call(ProjectionAsync.MapMethod.MakeGenericMethod(node.ValueType, typeof(object)),
                              node.Expression,
                              BoxLambda(node.ValueType));
    }

    static Expression BoxLambda(Type type)
    {
        var value = Expression.Parameter(type, "value");

        return Expression.Lambda(typeof(Func<,>).MakeGenericType(type, typeof(object)), Box(value), value);
    }

    static Expression Cast(Expression expression, Type type) =>
        expression.Type == type ? expression : Expression.Convert(expression, type);

    static Expression AsyncNullGuard(Expression source, Expression whenNotNull) =>
        Expression.Condition(
            Expression.Equal(source, Expression.Constant(null, source.Type)),
            Expression.Call(ProjectionAsync.FromResultMethod.MakeGenericMethod(typeof(object)), Expression.Constant(null, typeof(object))),
            whenNotNull);

    static Node BuildCall(Expression source, SelectionField field, INamingConvention naming)
    {
        if (!TryFindMethod(source.Type, field, naming, out var method))
            throw new FormatException(NotFound(source.Type, field, naming));

        return BuildCall(source, field, method, naming);
    }

    static string NotFound(Type type, SelectionField field, INamingConvention naming)
    {
        var hidden = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Any(x => IsQueryable(x) && NameMatches(field.Name, x.Name, naming));

        return hidden
            ? $"'{type.Name}.{field.Name}' is not queryable. Annotate it with [{nameof(SignalQueryableAttribute)}] to expose it."
            : $"'{type.Name}' has no queryable property or method named '{field.Name}'.";
    }

    static Node BuildCall(Expression source, SelectionField field, MethodInfo method, INamingConvention naming)
    {
        var parameters = method.GetParameters();
        var arguments = new Expression[parameters.Length];

        var unknown = field.ArgumentsOrEmpty.FirstOrDefault(x => !parameters.Any(p => NameMatches(x.Name, p.Name, naming)));

        if (unknown is not null)
            throw new FormatException($"'{field.Name}' has no argument named '{unknown.Name}'.");

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var supplied = field.ArgumentsOrEmpty.FirstOrDefault(x => NameMatches(x.Name, parameter.Name, naming));

            if (supplied is null)
            {
                if (!parameter.HasDefaultValue)
                    throw new FormatException($"Argument '{parameter.Name}' of '{field.Name}' is required.");

                arguments[i] = Expression.Constant(parameter.DefaultValue, parameter.ParameterType);
                continue;
            }

            arguments[i] = Expression.Constant(ConvertArgument(supplied, parameter, field), parameter.ParameterType);
        }

        Expression call = Expression.Call(source, method, arguments);

        if (TryGetTaskResultType(method.ReturnType, out var result))
            return Node.Async(method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
                ? Expression.Call(ProjectionAsync.FromTaskMethod.MakeGenericMethod(result), call)
                : call);

        return Node.Sync(call);
    }

    internal static bool TryGetTaskResultType(Type type, [NotNullWhen(true)] out Type? result)
    {
        result = null;

        if (!type.IsGenericType)
            return false;

        var definition = type.GetGenericTypeDefinition();

        if (definition != typeof(Task<>) && definition != typeof(ValueTask<>))
            return false;

        result = type.GetGenericArguments()[0];
        return true;
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

    static bool TryFindMethod(Type type, SelectionField field, INamingConvention naming, [NotNullWhen(true)] out MethodInfo? method)
    {
        var candidates = GetQueryableMethods(type).Where(x => NameMatches(field.Name, x.Name, naming)).ToList();

        if (candidates.Count > 1)
        {
            var matching = candidates.Where(x => Binds(x, field, naming)).ToList();

            if (matching.Count > 1)
                throw new FormatException($"'{field.Name}' is ambiguous between {matching.Count} overloads.");

            candidates = matching;
        }

        method = candidates.FirstOrDefault();
        return method is not null;
    }

    static bool Binds(MethodInfo method, SelectionField field, INamingConvention naming)
    {
        var parameters = method.GetParameters();

        return parameters.All(p => p.HasDefaultValue || field.ArgumentsOrEmpty.Any(a => NameMatches(a.Name, p.Name, naming)))
            && field.ArgumentsOrEmpty.All(a => parameters.Any(p => NameMatches(a.Name, p.Name, naming)));
    }

    static bool NameMatches(string queried, string? declared, INamingConvention naming) =>
        declared is not null
     && (string.Equals(queried, declared, StringComparison.Ordinal)
      || string.Equals(queried, naming.GetName(declared), StringComparison.Ordinal));

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
                                               && method.ReturnType != typeof(Task)
                                               && method.ReturnType != typeof(ValueTask);

    static Expression Unwrap(Expression source)
    {
        while (typeof(IReadOnlySignal).IsAssignableFrom(source.Type)
            && source.Type.GetProperty(nameof(IReadOnlySignal<object>.Value)) is { CanRead: true } value)
        {
            source = Expression.Property(source, value);
        }

        return source;
    }

    static Node BuildDictionaryProjection(Expression source, Type keyType, Type valueType, IReadOnlyList<SelectionField> fields, INamingConvention naming)
    {
        var pairType = typeof(KeyValuePair<,>).MakeGenericType(keyType, valueType);
        var element = Expression.Parameter(valueType, "value");
        var projected = Build(element, fields, naming);

        if (projected.IsAsync)
        {
            var selector = Expression.Lambda(typeof(Func<,>).MakeGenericType(valueType, typeof(ValueTask<object?>)), AsAsyncObject(projected), element);

            return Node.Async(Expression.Call(ProjectionAsync.DictionaryMethod.MakeGenericMethod(keyType, valueType),
                                              Cast(source, typeof(IEnumerable<>).MakeGenericType(pairType)),
                                              selector));
        }

        var pair = Expression.Parameter(pairType, "pair");

        var key = Expression.Call(Expression.Property(pair, nameof(KeyValuePair<int, int>.Key)), typeof(object).GetMethod(nameof(ToString))!);
        var value = Box(new ExpressionReplacer(element, Expression.Property(pair, nameof(KeyValuePair<int, int>.Value))).Visit(projected.Expression)!);

        var toDictionary = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                                             .First(m => m.Name == nameof(Enumerable.ToDictionary)
                                                      && m.GetGenericArguments().Length == 3
                                                      && m.GetParameters().Length == 4)
                                             .MakeGenericMethod(pairType, typeof(string), typeof(object));

        var dictionary = Expression.Call(
            toDictionary,
            source,
            Expression.Lambda(key, pair),
            Expression.Lambda(value, pair),
            Expression.Constant(null, typeof(IEqualityComparer<string>)));

        return Node.Sync(NullGuard(source, dictionary));
    }

    sealed class ExpressionReplacer(Expression from, Expression to) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node) => node == from ? to : base.Visit(node);
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

    static Node BuildSequenceProjection(Expression source, Type elementType, IReadOnlyList<SelectionField> fields, INamingConvention naming)
    {
        var element = Expression.Parameter(elementType, "element");
        var projected = Build(element, fields, naming);

        if (projected.IsAsync)
        {
            var asyncSelector = Expression.Lambda(typeof(Func<,>).MakeGenericType(elementType, typeof(ValueTask<object?>)), AsAsyncObject(projected), element);

            return Node.Async(Expression.Call(ProjectionAsync.SequenceMethod.MakeGenericMethod(elementType),
                                              Cast(source, typeof(IEnumerable<>).MakeGenericType(elementType)),
                                              asyncSelector));
        }

        var select = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                                       .First(m => m.Name == nameof(Enumerable.Select)
                                                && m.GetParameters().Length == 2
                                                && m.GetParameters()[1].ParameterType.GetGenericArguments().Length == 2)
                                       .MakeGenericMethod(elementType, typeof(object));

        var toList = typeof(Enumerable).GetMethod(nameof(Enumerable.ToList))!.MakeGenericMethod(typeof(object));

        var selector = Expression.Lambda(Box(projected.Expression), element);
        var sequence = Expression.Call(toList, Expression.Call(select, source, selector));

        return Node.Sync(NullGuard(source, sequence));
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

    static bool TryFindProperty(Type type, string queried, INamingConvention naming, [NotNullWhen(true)] out PropertyInfo? property)
    {
        foreach (var candidate in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (candidate.CanRead
             && naming.TryGetPropertyName(type, candidate, out var name)
             && string.Equals(queried, name, StringComparison.Ordinal))
            {
                property = candidate;
                return true;
            }

        property = null;
        return false;
    }

}
