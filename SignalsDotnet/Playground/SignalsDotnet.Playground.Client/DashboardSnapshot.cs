namespace SignalsDotnet.Playground.Client;

public sealed class DashboardSnapshot
{
    public string? Title { get; set; }
    public string? Status { get; set; }
    public int OnlineCount { get; set; }
    public int SensorCount { get; set; }
    public double Average { get; set; }
    public string? Summary { get; set; }
    public List<RankedSensor>? Ranked { get; set; }
}

public sealed class RankedSensor
{
    public string? Label { get; set; }
    public double Adjusted { get; set; }
}
