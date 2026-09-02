using Refit;
using SignalsDotnet.Playground;
using SignalsDotnet.Query;

namespace SignalsDotnet.Playground.Client;

public interface IDashboardApi
{
    [Get("/dashboard")]
    IAsyncEnumerable<T> GetDashboardValuesAsync<T>([Query(Format = "")] SignalComputedQuery<Dashboard, T> query, CancellationToken cancellationToken = default);
}
