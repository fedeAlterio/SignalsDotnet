using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SignalsDotnet.Playground.Client;

sealed class DashboardStreamWorker(DashboardStreamReader reader, ILogger<DashboardStreamWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var query = """
            {
                fullName
            }
            """;

        int received = 0;
        await foreach (var value in reader.ReadAsync(query, stoppingToken))
        {
            logger.LogInformation("[{Received}] {Value}", ++received, value);
        }
    }
}
