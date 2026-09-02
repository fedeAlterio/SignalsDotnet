using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SignalsDotnet.Playground;
using SignalsDotnet.Query;

namespace SignalsDotnet.Playground.Client;

sealed class DashboardStreamWorker(IDashboardApi api, ILogger<DashboardStreamWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var query = SignalComputedQuery.Create((Dashboard x) => new
        {
            Title = x.Title,
            Status = x.Status,
            Average = x.Average,
            Summary = x.Summary,
            Ranked = x
                .GetSensorsRankedAsync(2)
                .Await()
                .Select(s => new RankedSensor { Label = s.Label, Adjusted = s.Adjusted })
                .ToList()
        });

        await foreach (var value in api.GetDashboardValuesAsync(query, stoppingToken))
        {
            logger.LogInformation("[{Received}]", value);
        }
    }
}
