using System.Runtime.CompilerServices;

namespace SignalsDotnet.Tests.Helpers;

public static class MainThreadAwaitableExtensions
{
    public static MainThreadAwaitable SwitchToMainThread(this object _) => new();
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
    public void OnCompleted(Action action) => TestSingleThreadSynchronizationContext.Instance.Post(_ => action(), null);
    public void GetResult() { }
}