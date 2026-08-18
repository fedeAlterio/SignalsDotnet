using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SignalsDotnet.Query;

namespace SignalsDotnet.AspNetCore;

public static class SignalsQueryUiEndpointRouteBuilderExtensions
{
    const string ResourceName = "SignalsDotnet.AspNetCore.Ui.SignalsQueryUi.html";

    public static RouteHandlerBuilder MapSignalsQueryUi(this IEndpointRouteBuilder endpoints, string path)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(path);

        var discoveryPath = $"{path.TrimEnd('/')}/discovery";

        endpoints.MapGet(discoveryPath, () => Results.Json(new
        {
            islands = SignalIslandEndpointRegistry.All(endpoints)
                                                  .Select(x => new
                                                  {
                                                      path = x.Path,
                                                      name = x.IslandType.Name,
                                                      schema = SchemaBuilder.Build(x.IslandType)
                                                  })
        }, SignalsQueryExtensions.DefaultJsonOptions)).ExcludeFromDescription();

        var html = new Lazy<string>(() => BuildHtml(discoveryPath));

        return endpoints.MapGet(path, () => Results.Content(html.Value, "text/html; charset=utf-8"))
                        .ExcludeFromDescription();
    }

    static string BuildHtml(string discoveryPath)
    {
        using var stream = typeof(SignalsQueryUiEndpointRouteBuilderExtensions).Assembly.GetManifestResourceStream(ResourceName)
                        ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' was not found.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var html = reader.ReadToEnd();

        var script = $"<script>window.__SIGNALS_DISCOVERY__ = {JsonSerializer.Serialize(discoveryPath)};</script>";

        return html.Replace("</head>", script + "</head>", StringComparison.Ordinal);
    }
}

static class SchemaBuilder
{
    public static IDictionary<string, object?> Build(Type type) => Build(type, new HashSet<Type>(), 0);

    static IDictionary<string, object?> Build(Type type, HashSet<Type> seen, int depth)
    {
        var schema = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (depth > 4 || !seen.Add(type))
            return schema;

        var options = SignalsQueryExtensions.DefaultJsonOptions;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0
             || property.GetMethod is null
             || property.GetCustomAttribute<JsonIgnoreAttribute>() is not null
             || IsInfrastructure(property.PropertyType))
                continue;

            var name = options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
            var valueType = Unwrap(property.PropertyType);

            schema[name] = IsLeaf(valueType) ? null : Build(valueType, seen, depth + 1);
        }

        foreach (var method in SignalsQueryExtensions.GetQueryableMethods(type))
        {
            var name = options.PropertyNamingPolicy?.ConvertName(method.Name) ?? method.Name;

            if (schema.ContainsKey(name))
                continue;

            var returnType = Unwrap(method.ReturnType);
            var fields = IsLeaf(returnType) ? null : Build(returnType, seen, depth + 1);

            schema[name] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["$args"] = method.GetParameters()
                                  .Select(p => new Dictionary<string, object?>(StringComparer.Ordinal)
                                  {
                                      ["name"] = options.PropertyNamingPolicy?.ConvertName(p.Name ?? "") ?? p.Name,
                                      ["type"] = TypeName(p.ParameterType),
                                      ["optional"] = p.HasDefaultValue
                                  })
                                  .ToList(),
                ["$fields"] = fields
            };
        }

        seen.Remove(type);
        return schema;
    }

    static string TypeName(Type type)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;

        return value == typeof(int) || value == typeof(long) || value == typeof(short) ? "int"
             : value == typeof(double) || value == typeof(float) || value == typeof(decimal) ? "float"
             : value == typeof(bool) ? "bool"
             : value == typeof(string) ? "string"
             : value.Name;
    }

    static bool IsInfrastructure(Type type) => !IsProjectable(type)
                                            && (typeof(IReadOnlySignal).IsAssignableFrom(type)
                                             || typeof(INotifySignalChanged).IsAssignableFrom(type));

    static bool IsProjectable(Type type) => type.GetInterfaces()
                                                .Concat([type])
                                                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlySignal<>));

    static Type Unwrap(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            type = underlying;

        var signal = type.GetInterfaces()
                         .Concat([type])
                         .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlySignal<>));

        if (signal is not null)
            return Unwrap(signal.GetGenericArguments()[0]);

        if (type.IsArray)
            return type.GetElementType()!;

        if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
        {
            var element = type.GetInterfaces()
                              .Concat([type])
                              .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                              ?.GetGenericArguments()[0];

            if (element is not null)
                return Unwrap(element);
        }

        return type;
    }

    static bool IsLeaf(Type type) => type.IsPrimitive
                                  || type.IsEnum
                                  || type == typeof(string)
                                  || type == typeof(decimal)
                                  || type == typeof(DateTime)
                                  || type == typeof(DateTimeOffset)
                                  || type == typeof(TimeSpan)
                                  || type == typeof(Guid)
                                  || type == typeof(object);
}
