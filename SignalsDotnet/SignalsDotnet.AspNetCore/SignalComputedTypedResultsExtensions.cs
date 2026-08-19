using Microsoft.AspNetCore.Http;
using SignalsDotnet.Query;

namespace SignalsDotnet.AspNetCore;

public static class SignalComputedTypedResultsExtensions
{
    extension(TypedResults)
    {
        public static IResult SignalIslandComputed<T>(SignalIsland<T> island,
                                                SignalComputedQuery query,
                                                CancellationToken cancellationToken = default) where T : class
        {
            ArgumentNullException.ThrowIfNull(island);
            ArgumentNullException.ThrowIfNull(query);

            try
            {
                query.ToQuerySelectorExpression<T>();
            }
            catch (FormatException e)
            {
                return TypedResults.BadRequest<object>(new { error = e.Message });
            }

            return TypedResults.ServerSentEvents(island.ReadComputedValuesAsync(query, cancellationToken: cancellationToken));
        }

        public static IResult SignalIslandComputed<T>(SignalIsland<T> island,
                                                SignalComputedQueryString query,
                                                CancellationToken cancellationToken = default) where T : class
        {
            ArgumentNullException.ThrowIfNull(query);

            return TypedResults.SignalIslandComputed(island, query.Query, cancellationToken);
        }
    }
}
