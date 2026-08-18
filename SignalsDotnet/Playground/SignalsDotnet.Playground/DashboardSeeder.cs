using System.Collections.ObjectModel;
using SignalsDotnet.Query;

namespace SignalsDotnet.Playground;

sealed class DashboardSeeder(SignalIsland<Dashboard> island) : BackgroundService
{
    static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var dashboard = await island.SwitchToIslandContextAsync(stoppingToken);

        dashboard.Title = "Signals Playground";
        dashboard.Site = new Site
        {
            Name = "North Plant",
            Address = new Address
            {
                Street = "12 Harbour Road",
                City = "Trondheim",
                Region = new Region { Name = "Trøndelag", Country = "Norway", TimeZone = "Europe/Oslo" }
            },
            Owner = new Contact { FirstName = "Ada", LastName = "Nilsen", Email = "ada@example.com" }
        };

        dashboard.Sensors.Value =
        [
            NewSensor("north", "temperature", 21.5),
            NewSensor("south", "temperature", 27.1),
            NewSensor("attic", "humidity", 30.2)
        ];

        var random = new Random(42);
        var tick = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);

            await island.InvokeAsync(d =>
            {
                d.Ticks = ++tick;

                var sensors = d.Sensors.Value!;

                foreach (var sensor in sensors)
                    sensor.Reading = Math.Round(sensor.Reading + (random.NextDouble() - 0.5) * 3, 2);

                if (tick % 5 == 0)
                {
                    var toggled = sensors[random.Next(sensors.Count)];
                    toggled.IsOnline = !toggled.IsOnline;
                }

                if (tick % 12 == 0)
                {
                    if (sensors.Count > 2)
                        sensors.RemoveAt(sensors.Count - 1);
                    else
                        sensors.Add(NewSensor($"probe-{tick}", "pressure", 24));
                }
            }, stoppingToken);
        }
    }

    static Sensor NewSensor(string name, string kind, double reading) => new()
    {
        Name = name,
        Kind = kind,
        Reading = reading,
        IsOnline = true,
        Calibration = new Calibration { Scale = 1, Offset = 0, CalibratedBy = "factory" }
    };
}
