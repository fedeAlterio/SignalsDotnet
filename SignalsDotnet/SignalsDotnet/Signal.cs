using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reactive;
using System.Runtime.CompilerServices;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet;

public abstract partial class Signal
{
    static readonly ConcurrentDictionary<Thread, int> _untrackedThreads = new();
    static readonly ObservablesByKey<Thread, IReadOnlySignal> _propertyRequested = new();
    public static IObservable<IReadOnlySignal> PropertiesRequested(Thread thread) => _propertyRequested.When(thread);

    public static T Untracked<T>(Func<T> action)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        var currentThread = Thread.CurrentThread;
        var nestingCount = _untrackedThreads.AddOrUpdate(currentThread, static _ => 0, static (_, nestingCount) => nestingCount + 1);

        try
        {
            return action();
        }
        finally
        {
            if (nestingCount == 0)
            {
                _untrackedThreads.TryRemove(currentThread, out _);
            }
            else
            {
                _untrackedThreads[currentThread] = nestingCount - 1;
            }
        }
    }

    public static void Untracked(Action action)
    {
        Untracked(() =>
        {
            action();
            return Unit.Default;
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected void SetValue<T>(ref T field,
                               T value,
                               IEqualityComparer<T> equalityComparer,
                               bool raiseOnlyWhenChanged,
                               [CallerMemberName] string? propertyName = null)
    {
        if (raiseOnlyWhenChanged && equalityComparer.Equals(field, value))
            return;

        field = value;
        OnPropertyChanged(propertyName);
    }


    protected static T GetValue<T>(IReadOnlySignal property, in T value)
    {
        var currentThread = Thread.CurrentThread;
        if (!_untrackedThreads.ContainsKey(currentThread))
            _propertyRequested.Invoke(currentThread, property);

        return value;
    }
}