using System.Collections.Concurrent;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using FluentAssertions;

namespace SignalsDotnet.Tests;

public class ThrottleOneCycleTests
{
    [Fact]
    public async Task ThrottleOneCycleShouldNotNotifyInlineCalls()
    {
        await this.SwitchToMainThread();
        var subject = new Subject<int>();
        int totalNotifications = 0;
        subject.ThrottleOneCycle(new SynchronizationContextScheduler(SynchronizationContext.Current!))
               .Subscribe(_ => Interlocked.Increment(ref totalNotifications));

        for (var i = 0; i < 1000; i++)
        {
            subject.OnNext(i);
        }

        totalNotifications.Should().Be(0);
        await Task.Yield();
        totalNotifications.Should().Be(1);
        await Task.Yield();
        totalNotifications.Should().Be(1);
    }
}


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

public static class MainThreadAwaitableExtensions
{
    public static MainThreadAwaitable SwitchToMainThread(this object _) => new ();
}

/// <summary>
/// If awaited, force the continuation to run on a Single-threaded synchronization context.
/// That's the exact behavior of Wpf Synchronization Context (DispatcherSynchronizationContext)
/// So basically:
/// 1) after the await we switch thread.
/// 2) Every other continuation will run on the same thread as it happens in Wpf.
/// </summary>
public readonly struct MainThreadAwaitable : INotifyCompletion
{
    public MainThreadAwaitable GetAwaiter() => this;
    public bool IsCompleted => SynchronizationContext.Current == TestSingleThreadSynchronizationContext.Instance;
    public void OnCompleted(Action action) => TestSingleThreadSynchronizationContext.Instance.Send(_ => action(), null);
    public void GetResult(){}
}