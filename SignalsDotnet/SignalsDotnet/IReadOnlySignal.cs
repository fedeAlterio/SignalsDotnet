using System.ComponentModel;
using R3;

namespace SignalsDotnet;

public interface IReadOnlySignal : INotifyPropertyChanged
{
    Observable<Unit> ValuesUnit { get; }
    Observable<Unit> FutureValuesUnit => ValuesUnit.Skip(1);
    object? Value { get; }
    object? UntrackedValue { get; }
}

public interface IReadOnlySignal<out T> : IReadOnlySignal
{
    new T Value { get; }
    new T UntrackedValue { get; }
    object? IReadOnlySignal.Value => Value;
}

public static class ReadOnlySignalEx
{
    public static Observable<T> Values<T>(this IReadOnlySignal<T> @this)
    {
        return @this.ValuesUnit.Select(_ => @this.Value);
    }
}

public interface IAsyncReadOnlySignal<out T> : IReadOnlySignal<T>
{
    IReadOnlySignal<bool> IsComputing { get; }
}