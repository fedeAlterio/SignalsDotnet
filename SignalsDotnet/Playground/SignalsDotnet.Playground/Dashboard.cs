using System.Collections.ObjectModel;
using SignalsDotnet;

namespace SignalsDotnet.Playground;

[GenerateSignals]
public partial class Sensor
{
    public partial string Name { get; set; }
    public partial double Reading { get; set; }
    public partial bool IsOnline { get; set; }
}

[GenerateSignals]
public partial class Dashboard
{
    public partial string Title { get; set; }
    public partial int Ticks { get; set; }

    [SignalIgnore]
    public CollectionSignal<ObservableCollection<Sensor>> Sensors { get; } = new();

    [Computed]
    int ComputeOnlineCount() => Sensors.Value?.Count(x => x.IsOnline) ?? 0;

    [Computed]
    double ComputeAverage()
    {
        var online = Sensors.Value?.Where(x => x.IsOnline).ToArray() ?? [];

        return online.Length == 0 ? 0 : Math.Round(online.Average(x => x.Reading), 2);
    }
}
