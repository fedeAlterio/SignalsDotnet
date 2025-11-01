using System.ComponentModel;
using R3;

namespace SignalsDotnet;

public interface IReadOnlySignal : INotifyPropertyChanged
{
    Observable<Unit> Values { get; }
    Observable<Unit> FutureValues => Values.Skip(1);
    object? Value { get; }
    object? UntrackedValue { get; }
}

public interface IReadOnlySignal<T> : IReadOnlySignal
{
    new Observable<T> Values { get; }
    new Observable<T> FutureValues { get; }
    new T Value { get; }
    new T UntrackedValue { get; }
    object? IReadOnlySignal.Value => Value;
}

public interface ISignal<T> : IReadOnlySignal<T>
{
    new T Value { get; set; }
}

public interface IAsyncReadOnlySignal<T> : IReadOnlySignal<T>
{
    IReadOnlySignal<bool> IsComputing { get; }
}

public interface IAsyncSignal<T> : IAsyncReadOnlySignal<T>, ISignal<T>;
