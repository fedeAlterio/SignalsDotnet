using SignalsDotnet.Query;

namespace SignalsDotnet.Playground;

static class DashboardStreamEndpoint
{
    public static IEndpointRouteBuilder MapDashboardStreamEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dashboard/stream", (SignalIsland<Dashboard> island, string? query, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(query))
                return Results.BadRequest(new { error = "A 'query' parameter is required." });

            if (!SignalsQuery.TryParse(query, out var selection))
                return Results.BadRequest(new { error = $"'{query}' is not a valid query." });

            return TypedResults.ServerSentEvents(island.ReadComputedValuesAsync(selection, cancellationToken: cancellationToken));
        });

        return endpoints;
    }
}
