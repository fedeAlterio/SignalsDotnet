using R3Async;
using R3Async.Subjects;

namespace SignalsDotnet.SignalsStore.Tests.Helpers;

sealed class ControllableSubject<T> : ISubject<T>
{
    readonly ISubject<T> _inner = Subject.Create<T>();
    readonly List<T> _received = new();
    readonly object _gate = new();

    TaskCompletionSource _publishGate = new();
    int _inFlight;

    public ControllableSubject() => ReleasePublish();

    public bool InFlight => Volatile.Read(ref _inFlight) > 0;

    public IReadOnlyList<T> Received
    {
        get
        {
            lock (_gate)
                return _received.ToArray();
        }
    }

    public void BlockPublish() => _publishGate = new TaskCompletionSource();

    public void ReleasePublish() => _publishGate.TrySetResult();

    public AsyncObservable<T> Values => _inner.Values;

    public async ValueTask OnNextAsync(T value, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _inFlight);
        try
        {
            await _publishGate.Task.ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }

        lock (_gate)
            _received.Add(value);

        await _inner.OnNextAsync(value, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken) =>
        _inner.OnErrorResumeAsync(error, cancellationToken);

    public ValueTask OnCompletedAsync(Result result) => _inner.OnCompletedAsync(result);
}
