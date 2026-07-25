namespace SignalsDotnet.SignalsStore;

public interface ISignalProxy<T> : IReadOnlySignal<T>
{
    IReadOnlySignal<ConnectionState> ConnectionState { get; }
    ValueTask ConnectAsync(CancellationToken cancellationToken);
}