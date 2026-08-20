using System.Collections.ObjectModel;
using SignalsDotnet;
using SignalsDotnet.Query;

namespace SignalsDotnet.Playground;

[GenerateSignals]
public partial class Dashboard
{
    public partial string Title { get; set; }
    public partial int Ticks { get; set; }
    public partial Site Site { get; set; }

    [SignalIgnore]
    public CollectionSignal<ObservableCollection<Sensor>> Sensors { get; } = new();

    [SignalQueryable]
    public Sensor? GetSensorByIndex(int index) => Sensors.Value?.ElementAtOrDefault(index);

    [SignalQueryable]
    public IReadOnlyList<Sensor> GetSensorsAbove(double threshold, bool onlineOnly = true) =>
        Sensors.Value?.Where(x => (!onlineOnly || x.IsOnline) && x.Reading > threshold).ToList() ?? [];

    [SignalQueryable]
    public async ValueTask<IReadOnlyList<Sensor>> GetSensorsRankedAsync(int take = 3)
    {
        await Task.Delay(3000);

        return Sensors.Value?.Where(x => x.IsOnline).OrderByDescending(x => x.Reading).Take(take).ToList() ?? [];
    }

    [SignalQueryable]
    public Sensor? FindSensor(string name) =>
        Sensors.Value?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    [Computed]
    int ComputeSensorCount() => Sensors.Value?.Count ?? 0;

    [Computed]
    int ComputeOnlineCount() => Sensors.Value?.Count(x => x.IsOnline) ?? 0;

    [Computed]
    int ComputeOfflineCount() => SensorCount - OnlineCount;

    [Computed]
    double ComputeAverage()
    {
        var online = Sensors.Value?.Where(x => x.IsOnline).ToList() ?? [];

        return online.Count == 0 ? 0 : Math.Round(online.Average(x => x.Reading), 2);
    }

    [Computed]
    double ComputePeak()
    {
        var online = Sensors.Value?.Where(x => x.IsOnline).ToList() ?? [];

        return online.Count == 0 ? 0 : Math.Round(online.Max(x => x.Reading), 2);
    }

    [Computed]
    string ComputeStatus() => OnlineCount == 0 ? "offline"
                            : OfflineCount > 0 ? "degraded"
                            : "healthy";

    [Computed]
    string ComputeSummary() => $"{Site.Name} — {OnlineCount}/{SensorCount} online, avg {Average}";
}

[GenerateSignals]
public partial class Site
{
    public partial string Name { get; set; }
    public partial Address Address { get; set; }
    public partial Contact Owner { get; set; }

    [Computed]
    string ComputeLabel() => $"{Name} ({Address.City})";
}

[GenerateSignals]
public partial class Address
{
    public partial string Street { get; set; }
    public partial string City { get; set; }
    public partial Region Region { get; set; }

    [Computed]
    string ComputeFormatted() => $"{Street}, {City}, {Region.Country}";
}

[GenerateSignals]
public partial class Region
{
    public partial string Name { get; set; }
    public partial string Country { get; set; }
    public partial string TimeZone { get; set; }
}

[GenerateSignals]
public partial class Contact
{
    public partial string FirstName { get; set; }
    public partial string LastName { get; set; }
    public partial string Email { get; set; }

    [Computed]
    string ComputeFullName() => $"{FirstName} {LastName}";
}

[GenerateSignals]
public partial class Sensor
{
    public partial string Name { get; set; }
    public partial string Kind { get; set; }
    public partial double Reading { get; set; }
    public partial bool IsOnline { get; set; }
    public partial Calibration Calibration { get; set; }

    [Computed]
    string ComputeLabel() => $"{Name} ({Kind})";

    [Computed]
    double ComputeAdjusted() => Math.Round(Reading * Calibration.Scale + Calibration.Offset, 2);

    [Computed]
    string ComputeState() => !IsOnline ? "offline"
                           : Adjusted > 28 ? "high"
                           : Adjusted < 18 ? "low"
                           : "normal";

    [SignalQueryable]
    public double ReadingIn(string unit) => unit switch
    {
        "F" => Math.Round(Reading * 9 / 5 + 32, 2),
        "K" => Math.Round(Reading + 273.15, 2),
        _ => Reading
    };

    [SignalQueryable]
    public bool IsAbove(double threshold) => Reading > threshold;
}

[GenerateSignals]
public partial class Calibration
{
    public partial double Scale { get; set; }
    public partial double Offset { get; set; }
    public partial string CalibratedBy { get; set; }
}
