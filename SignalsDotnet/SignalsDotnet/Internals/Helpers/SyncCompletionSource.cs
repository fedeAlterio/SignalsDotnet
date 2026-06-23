using System.Runtime.CompilerServices;
using R3;

namespace SignalsDotnet.Internals.Helpers;

internal sealed class SyncCompletionSource : INotifyCompletion
{
    Action? _continuation;
    public SyncCompletionSource GetAwaiter() => this;
    public bool IsCompleted => ReferenceEquals(Volatile.Read(ref _continuation), ActionStub.Nop);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnCompleted(Action continuation)
    {
        Action? original = Interlocked.CompareExchange(ref _continuation, continuation, null);
        if (original is null) return;
        if (ReferenceEquals(original, ActionStub.Nop))
            continuation();
        else
            throw new InvalidOperationException("Double await");
    }

    public void GetResult() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetCompleted(Unit unit) => Interlocked.Exchange(ref _continuation, ActionStub.Nop)?.Invoke();
}