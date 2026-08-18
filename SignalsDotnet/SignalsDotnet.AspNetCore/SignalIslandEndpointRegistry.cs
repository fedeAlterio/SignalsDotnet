using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Routing;

namespace SignalsDotnet.AspNetCore;

sealed record SignalIslandEndpoint(string Path, Type IslandType);

static class SignalIslandEndpointRegistry
{
    static readonly ConditionalWeakTable<IEndpointRouteBuilder, List<SignalIslandEndpoint>> Endpoints = new();

    public static void Add(IEndpointRouteBuilder endpoints, string path, Type islandType)
    {
        var registered = Endpoints.GetOrCreateValue(endpoints);

        lock (registered)
        {
            if (!registered.Any(x => x.Path == path))
                registered.Add(new SignalIslandEndpoint(path, islandType));
        }
    }

    public static IReadOnlyList<SignalIslandEndpoint> All(IEndpointRouteBuilder endpoints)
    {
        if (!Endpoints.TryGetValue(endpoints, out var registered))
            return [];

        lock (registered)
            return registered.ToArray();
    }
}
