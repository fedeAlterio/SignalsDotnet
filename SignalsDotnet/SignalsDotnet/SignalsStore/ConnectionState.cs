namespace SignalsDotnet.SignalsStore;

public abstract record ConnectionState
{
    private ConnectionState() { }

    public sealed record Disconnected(Exception? Error = null) : ConnectionState;
    public sealed record Connecting : ConnectionState;
    public sealed record Connected : ConnectionState;
}