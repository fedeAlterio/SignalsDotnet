using System.ComponentModel;
using System.Reactive;

namespace SignalsDotnet;

public interface IReadOnlySignal : INotifyPropertyChanged
{
    IObservable<Unit> Changed { get; }
    object? Value { get; }
    object? UntrackedValue { get; }
}

public interface IReadOnlySignal<out T> : IObservable<T>, IReadOnlySignal
{
    new T Value { get; }
    new T UntrackedValue { get; }
    object? IReadOnlySignal.Value => Value;
}

public interface IAsyncReadOnlySignal<out T> : IReadOnlySignal<T>
{
    IReadOnlySignal<bool> IsComputing { get; }
}