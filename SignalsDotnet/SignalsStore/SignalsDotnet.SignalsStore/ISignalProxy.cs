using R3Async.Subjects;

namespace SignalsDotnet.SignalsStore;

public interface ISignalProxy<T> : IReadOnlySignal<T>
{
    string Id { get; }
    ISubject<T> Subject { get; }
    IReadOnlySignal<ConnectionState> ConnectionState { get; }
    IReadOnlySignal<bool> HasValueObserver { get; }
    ValueTask EnsureConnectedAsync(CancellationToken cancellationToken);
}
