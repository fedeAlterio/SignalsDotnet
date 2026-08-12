using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() => Volatile.Write(ref _continuation, null);
}

internal sealed class SyncCompletionSource<T> : IAwaitable<T>, IAwaiter<T>
{
    Action? _continuation;
    T _result = default!;

    public SyncCompletionSource<T> GetAwaiter() => this;
    IAwaiter<T> IAwaitable<T>.GetAwaiter() => this;
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

    public T GetResult()
    {
        if (Error is { } error)
            ExceptionDispatchInfo.Capture(error).Throw();

        return _result;
    }

    internal Exception? Error { get; set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResult(T result)
    {
        _result = result;
        Interlocked.Exchange(ref _continuation, ActionStub.Nop)?.Invoke();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetCompleted() => Interlocked.Exchange(ref _continuation, ActionStub.Nop)?.Invoke();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() => Volatile.Write(ref _continuation, null);
}
