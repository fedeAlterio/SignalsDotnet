using System.Reflection;
using Microsoft.AspNetCore.Http;
using SignalsDotnet.Query;

namespace SignalsDotnet.AspNetCore;

public sealed class SignalIslandMetadata(Type islandType, string queryParameterName, string? name = null)
{
    public Type IslandType { get; } = islandType ?? throw new ArgumentNullException(nameof(islandType));

    public string QueryParameterName { get; } = queryParameterName ?? throw new ArgumentNullException(nameof(queryParameterName));

    public string Name { get; } = name is { Length: > 0 } ? name : islandType?.Name ?? throw new ArgumentNullException(nameof(islandType));

    internal static SignalIslandMetadata? For(MethodInfo? handler, SignalIslandDiscoveryOptions options)
    {
        if (handler is null)
            return null;

        var parameters = handler.GetParameters();

        if (parameters.Select(x => x.ParameterType).FirstOrDefault(IsIsland)?.GetGenericArguments()[0] is not { } islandType)
            return null;

        var queryParameterName = parameters.FirstOrDefault(x => x.ParameterType == typeof(SignalComputedQueryString))?.Name;

        return new SignalIslandMetadata(islandType,
                                        queryParameterName is { Length: > 0 } ? queryParameterName : SignalComputedQueryString.ParameterName,
                                        options.Name);
    }

    internal static SignalIslandMetadata? For(Endpoint endpoint) => endpoint.Metadata.GetMetadata<SignalIslandMetadata>();

    static bool IsIsland(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SignalIsland<>);
}
