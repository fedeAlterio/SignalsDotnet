using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SignalsDotnet.Query;

namespace SignalsDotnet.AspNetCore;

public static class SignalIslandEndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder MapSignalIsland<T>(this IEndpointRouteBuilder endpoints, string path, JsonSerializerOptions? options = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(path);

        SignalIslandEndpointRegistry.Add(endpoints, path, typeof(T));

        return endpoints.MapGet(path, (SignalIsland<T> island, string? query, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(query))
                return Results.BadRequest(new { error = "A 'query' parameter is required." });

            if (!SignalsQuery.TryParse(query, out var selection))
                return Results.BadRequest(new { error = $"'{query}' is not a valid query." });

            try
            {
                selection.ToQuerySelectorExpression<T>(options);
            }
            catch (FormatException e)
            {
                return Results.BadRequest(new { error = e.Message });
            }

            return TypedResults.ServerSentEvents(island.ReadComputedValuesAsync(selection, options, cancellationToken));
        });
    }
}
