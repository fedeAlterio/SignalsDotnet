using System.Collections.Concurrent;

namespace SignalsDotnet.Tests.Helpers;

internal sealed class TestSingleThreadSynchronizationContext : SynchronizationContext
{
    public Thread MainThread { get; }
    readonly BlockingCollection<(SendOrPostCallback callback, object? state)> _callbacksWithState = [];

    public TestSingleThreadSynchronizationContext()
    {
        MainThread = new Thread(MainThreadLoop)
        {
            IsBackground = true
        };

        MainThread.Start();
    }

    public static TestSingleThreadSynchronizationContext Instance { get; } = new();

    void MainThreadLoop()
    {
        SetSynchronizationContext(this);

        foreach (var (callback, state) in _callbacksWithState.GetConsumingEnumerable())
            callback.Invoke(state);
    }

    public override void Post(SendOrPostCallback callback, object? state)
    {
        _callbacksWithState.Add((callback, state));
    }

    public override void Send(SendOrPostCallback callback, object? state)
    {
        if (Current == this)
        {
            callback(state);
            return;
        }

        _callbacksWithState.Add((callback, state));
    }
}