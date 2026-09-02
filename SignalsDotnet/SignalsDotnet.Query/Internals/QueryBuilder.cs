using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace SignalsDotnet.Query.Internals;

static class QueryBuilder
{
    internal static IReadOnlyList<SelectionField> Build(LambdaExpression selector, INamingConvention naming)
    {
        if (selector.Parameters.Count != 1)
            throw new NotSupportedException("The selector must take a single parameter.");

        var parameter = selector.Parameters[0];
        var body = Unwrap(selector.Body);

        if (TryBuildSelectionSet(body, parameter, parameter.Type, naming, out var fields))
            return fields;

        return [BuildField(body, parameter, parameter.Type, naming, null)];
    }

    static bool TryBuildSelectionSet(Expression body,
                                     ParameterExpression root,
                                     Type sourceType,
                                     INamingConvention naming,
                                     [NotNullWhen(true)] out IReadOnlyList<SelectionField>? fields)
    {
        switch (body)
        {
            case NewExpression { Members: { Count: > 0 } members } anonymous:
                fields = members.Select((member, i) => BuildField(Unwrap(anonymous.Arguments[i]), root, sourceType, naming, member.Name))
                                .ToList();
                return true;

            case MemberInitExpression init:
                if (init.NewExpression.Arguments.Count != 0)
                    throw new NotSupportedException($"'{init}' must use a parameterless constructor.");

                fields = init.Bindings
                             .Select(binding => binding is MemberAssignment assignment
                                 ? BuildField(Unwrap(assignment.Expression), root, sourceType, naming, assignment.Member.Name)
                                 : throw new NotSupportedException($"'{binding.Member.Name}' must be a simple assignment."))
                             .ToList();
                return true;

            default:
                fields = null;
                return false;
        }
    }

    static SelectionField BuildField(Expression expression,
                                     ParameterExpression root,
                                     Type sourceType,
                                     INamingConvention naming,
                                     string? key)
    {
        var path = new List<Expression>();
        var current = expression;

        while (true)
        {
            current = Unwrap(current);

            if (current == root)
                break;

            switch (current)
            {
                case MemberExpression { Expression: not null, Member: PropertyInfo } member:
                    path.Add(member);
                    current = member.Expression;
                    continue;

                case MethodCallExpression { Object: not null } call:
                    path.Add(call);
                    current = call.Object;
                    continue;

                case MethodCallExpression { Object: null } call when IsProjectionOperator(call):
                    return BuildProjectionOperator(call, root, sourceType, naming, key);

                default:
                    throw new NotSupportedException(Unsupported(expression));
            }
        }

        if (path.Count == 0)
            throw new NotSupportedException(Unsupported(expression));

        path.Reverse();

        SelectionField? field = null;

        for (var i = path.Count - 1; i >= 0; i--)
        {
            IReadOnlyList<SelectionField> children = field is null ? [] : [field];
            var owner = i == 0 ? sourceType : Unwrap(Owner(path[i])).Type;

            field = Describe(path[i], owner, naming, i == 0 ? key : null, children);
        }

        return field!;
    }

    static Expression Owner(Expression step) => step switch
    {
        MemberExpression member => member.Expression!,
        MethodCallExpression call => call.Object!,
        _ => throw new NotSupportedException(Unsupported(step))
    };

    static SelectionField Describe(Expression step,
                                   Type sourceType,
                                   INamingConvention naming,
                                   string? key,
                                   IReadOnlyList<SelectionField> children)
    {
        var owner = ElementTypeOf(UnwrapSignalType(sourceType));

        if (step is MemberExpression member)
        {
            var name = JsonNameOf((PropertyInfo)member.Member, owner, naming);

            return new SelectionField(name, Alias(key, name, naming), null, children);
        }

        var call = (MethodCallExpression)step;
        var method = call.Method;

        if (!ProjectionBuilder.GetQueryableMethods(owner).Contains(method))
            throw new NotSupportedException($"'{owner.Name}.{method.Name}' is not queryable. Annotate it with [{nameof(SignalQueryableAttribute)}] to expose it.");

        var called = JsonNameOf(method.Name, naming);
        var parameters = method.GetParameters();
        var arguments = new List<SelectionArgument>(parameters.Length);

        for (var i = 0; i < parameters.Length; i++)
            arguments.Add(new SelectionArgument(JsonNameOf(parameters[i].Name!, naming), Evaluate(call.Arguments[i], parameters[i], method)));

        return new SelectionField(called, Alias(key, called, naming), arguments.Count == 0 ? null : arguments, children);
    }

    static string? Alias(string? key, string name, INamingConvention naming)
    {
        if (key is null)
            return null;

        var converted = JsonNameOf(key, naming);

        return string.Equals(converted, name, StringComparison.Ordinal) ? null : converted;
    }

    static bool IsProjectionOperator(MethodCallExpression call) =>
        call.Method.DeclaringType == typeof(Enumerable)
     && call.Method.Name == nameof(Enumerable.Select)
     && call.Arguments.Count == 2;

    static SelectionField BuildProjectionOperator(MethodCallExpression call,
                                                  ParameterExpression root,
                                                  Type sourceType,
                                                  INamingConvention naming,
                                                  string? key)
    {
        var lambda = (LambdaExpression)StripQuotes(call.Arguments[1]);
        var source = BuildField(Unwrap(call.Arguments[0]), root, sourceType, naming, key);

        if (source.Children.Count != 0)
            throw new NotSupportedException(Unsupported(call));

        var element = lambda.Parameters[0];
        var body = Unwrap(lambda.Body);

        IReadOnlyList<SelectionField> children = TryBuildSelectionSet(body, element, element.Type, naming, out var set)
            ? set
            : [BuildField(body, element, element.Type, naming, null)];

        return source with { Children = children };
    }

    static object? Evaluate(Expression argument, ParameterInfo parameter, MethodInfo method)
    {
        var value = argument is ConstantExpression constant
            ? constant.Value
            : Expression.Lambda<Func<object?>>(Expression.Convert(argument, typeof(object))).Compile()();

        return Literal(value, parameter, method);
    }

    static object? Literal(object? value, ParameterInfo parameter, MethodInfo method) => value switch
    {
        null => null,
        bool or string or long => value,
        sbyte or byte or short or ushort or int or uint => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        ulong u when u <= long.MaxValue => (long)u,
        float or double or decimal => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        Enum e => Enum.GetName(e.GetType(), e) ?? throw new NotSupportedException($"Argument '{parameter.Name}' of '{method.Name}' is not a named value of '{e.GetType().Name}'."),
        char c => c.ToString(),
        Guid or TimeSpan or DateTime or DateTimeOffset => Format(value),
        _ => throw new NotSupportedException($"Argument '{parameter.Name}' of '{method.Name}' is of unsupported type '{value.GetType().Name}'.")
    };

    static string Format(object value) => value switch
    {
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()!
    };

    static Expression StripQuotes(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Quote } quote)
            expression = quote.Operand;

        return expression;
    }

    static Expression Unwrap(Expression expression)
    {
        while (true)
        {
            if (expression is UnaryExpression { NodeType: ExpressionType.Quote } quote)
            {
                expression = quote.Operand;
                continue;
            }

            if (expression is MethodCallExpression { Object: null, Arguments.Count: 1 } materializer
             && materializer.Method.DeclaringType == typeof(Enumerable)
             && materializer.Method.Name is nameof(Enumerable.ToList) or nameof(Enumerable.ToArray))
            {
                expression = materializer.Arguments[0];
                continue;
            }

            if (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary
             && (unary.Type == typeof(object) || unary.Type.IsAssignableFrom(unary.Operand.Type)))
            {
                expression = unary.Operand;
                continue;
            }

            if (expression is MemberExpression { Expression: not null } member
             && member.Member.Name == nameof(IReadOnlySignal<object>.Value)
             && (typeof(IReadOnlySignal).IsAssignableFrom(member.Expression.Type) || IsKeyValuePair(member.Expression.Type)))
            {
                expression = member.Expression;
                continue;
            }

            if (expression is MethodCallExpression { Object: null, Arguments.Count: 1 } call
             && call.Method.DeclaringType == typeof(SignalQueryAwaitExtensions)
             && call.Method.Name == nameof(SignalQueryAwaitExtensions.Await))
            {
                expression = call.Arguments[0];
                continue;
            }

            return expression;
        }
    }

    static bool IsAwaitable(Type type) =>
        type.IsGenericType
     && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>));

    static bool IsKeyValuePair(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);

    static Type UnwrapSignalType(Type type)
    {
        while (typeof(IReadOnlySignal).IsAssignableFrom(type)
            && type.GetProperty(nameof(IReadOnlySignal<object>.Value)) is { CanRead: true } value)
        {
            type = value.PropertyType;
        }

        return type;
    }

    static Type ElementTypeOf(Type type)
    {
        if (IsAwaitable(type))
            type = type.GetGenericArguments()[0];

        if (IsKeyValuePair(type))
            return type.GetGenericArguments()[1];

        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
            return type;

        if (type.IsArray)
            return type.GetElementType()!;

        var dictionary = type.GetInterfaces()
                             .Prepend(type)
                             .FirstOrDefault(i => i.IsGenericType
                                               && (i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                                                || i.GetGenericTypeDefinition() == typeof(IDictionary<,>)));

        if (dictionary is not null)
            return dictionary.GetGenericArguments()[1];

        var enumerable = type.GetInterfaces()
                             .Prepend(type)
                             .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerable is null ? type : enumerable.GetGenericArguments()[0];
    }

    static string JsonNameOf(PropertyInfo property, Type owner, INamingConvention naming) =>
        naming.TryGetPropertyName(owner, property, out var name)
            ? name!
            : throw new NotSupportedException($"'{owner.Name}.{property.Name}' is not exposed and cannot be queried.");

    static string JsonNameOf(string name, INamingConvention naming) =>
        naming.GetName(name);

    static string Unsupported(Expression expression) =>
        $"'{expression}' is not a selection and cannot be translated into a query.";

    internal static string Write(IReadOnlyList<SelectionField> fields)
    {
        var text = new StringBuilder();

        WriteSelectionSet(text, fields);

        return text.ToString();
    }

    static void WriteSelectionSet(StringBuilder text, IReadOnlyList<SelectionField> fields)
    {
        text.Append("{ ");

        for (var i = 0; i < fields.Count; i++)
        {
            if (i != 0)
                text.Append(' ');

            WriteField(text, fields[i]);
        }

        text.Append(" }");
    }

    static void WriteField(StringBuilder text, SelectionField field)
    {
        if (field.Alias is not null)
            text.Append(field.Alias).Append(": ");

        text.Append(field.Name);

        if (field.ArgumentsOrEmpty.Count != 0)
        {
            text.Append('(');

            for (var i = 0; i < field.ArgumentsOrEmpty.Count; i++)
            {
                if (i != 0)
                    text.Append(", ");

                var argument = field.ArgumentsOrEmpty[i];

                text.Append(argument.Name).Append(": ");
                WriteValue(text, argument.Value);
            }

            text.Append(')');
        }

        if (field.Children.Count != 0)
        {
            text.Append(' ');
            WriteSelectionSet(text, field.Children);
        }
    }

    static void WriteValue(StringBuilder text, object? value)
    {
        switch (value)
        {
            case null:
                text.Append("null");
                return;

            case bool flag:
                text.Append(flag ? "true" : "false");
                return;

            case string s:
                WriteString(text, s);
                return;

            case double d:
                if (!double.IsFinite(d))
                    throw new NotSupportedException($"'{d}' cannot be written as a query value.");

                var number = d.ToString("R", CultureInfo.InvariantCulture);

                text.Append(number);

                if (number.AsSpan().IndexOfAny('.', 'E', 'e') < 0)
                    text.Append(".0");

                return;

            default:
                text.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
        }
    }

    static void WriteString(StringBuilder text, string value)
    {
        text.Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\b':
                    text.Append("\\b");
                    break;
                case '\f':
                    text.Append("\\f");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                default:
                    if (char.IsControl(c))
                        text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        text.Append(c);

                    break;
            }
        }

        text.Append('"');
    }
}
