using SignalsDotnet.Query;

namespace SignalsDotnet.Playground;

sealed class DashboardSeeder(SignalIsland<Dashboard> island) : BackgroundService
{
    static readonly TimeSpan Interval = TimeSpan.FromSeconds(4);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var dashboard = await island.SwitchToSignalContextAsync(stoppingToken);

        dashboard.Title = "Signals Playground";
        dashboard.Sensors.Value =
        [
            new Sensor { Name = "north", Reading = 21.5, IsOnline = true },
            new Sensor { Name = "south", Reading = 19.0, IsOnline = true },
            new Sensor { Name = "attic", Reading = 30.2, IsOnline = false }
        ];

        using var timer = new PeriodicTimer(Interval);
        var random = new Random(42);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            dashboard.Ticks++;

            var sensors = dashboard.Sensors.Value;

            var sensor = sensors[random.Next(sensors.Count)];

            sensor.Reading = Math.Round(15 + random.NextDouble() * 20, 2);

            if (random.Next(4) == 0)
                sensor.IsOnline = !sensor.IsOnline;
        }
    }
}
