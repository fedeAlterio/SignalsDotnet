using System.Runtime.CompilerServices;

namespace SignalsDotnet;

public interface IAwaiter : INotifyCompletion
{
    bool IsCompleted { get; }
    void GetResult();
}

public interface IAwaiter<out T> : INotifyCompletion
{
    bool IsCompleted { get; }
    T GetResult();
}

public interface IAwaitable
{
    IAwaiter GetAwaiter();
}

public interface IAwaitable<out T>
{
    IAwaiter<T> GetAwaiter();
}
