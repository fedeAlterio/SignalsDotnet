using R3Async;

namespace SignalsDotnet.SignalsStore.Tests.Helpers;

/// <summary>
/// A test double for the upstream <see cref="AsyncObservable{T}"/> whose connect and disconnect
/// are held open until the test releases them, so lifecycle ordering can be asserted precisely
/// instead of inferred from timing.
/// </summary>
sealed class ControllableUpstream<T> : AsyncObservable<T>
{
    readonly TaskCompletionSource _connectGate = new();
    readonly TaskCompletionSource _disconnectGate = new();

    AsyncObserver<T>? _observer;

    public int ConnectCount { get; private set; }
    public int DisconnectCount { get; private set; }
    public bool ConnectThrows { get; set; }

    public void ReleaseConnect() => _connectGate.TrySetResult();
    public void ReleaseDisconnect() => _disconnectGate.TrySetResult();

    public async ValueTask EmitAsync(T value, CancellationToken cancellationToken = default)
    {
        var observer = _observer ?? throw new InvalidOperationException("Not connected.");
        await observer.OnNextAsync(value, cancellationToken);
    }

    public async ValueTask CompleteAsync(Exception? error = null)
    {
        var observer = _observer ?? throw new InvalidOperationException("Not connected.");

        // A completed upstream has nothing left to hold open: release the disconnect gate so
        // the teardown this triggers (e.g. Share's ResetOnRefCountZero) doesn't block forever
        // waiting for a disconnect the test never asked to gate.
        ReleaseDisconnect();
        await observer.OnCompletedAsync(error is null ? Result.Success : Result.Failure(error));
    }

    protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
    {
        await _connectGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (ConnectThrows)
            throw new InvalidOperationException("Simulated connect failure.");

        ConnectCount++;
        _observer = observer;
        return new Subscription(this);
    }

    sealed class Subscription(ControllableUpstream<T> owner) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await owner._disconnectGate.Task.ConfigureAwait(false);
            owner.DisconnectCount++;
        }
    }
}
