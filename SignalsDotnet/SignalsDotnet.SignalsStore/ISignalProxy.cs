namespace SignalsDotnet.SignalsStore;

public interface ISignalProxy<T> : ISignal<T>
{
    string Id { get; }
    IReadOnlySignal<ConnectionState> ConnectionState { get; }
    IReadOnlySignal<bool> HasValueObserver { get; }
    ValueTask EnsureConnectedAsync(CancellationToken cancellationToken);
}
