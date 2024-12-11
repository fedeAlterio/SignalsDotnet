using System.ComponentModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;

namespace SignalsDotnet;

public abstract partial class Signal
{
    static readonly object _untrackedLocker = new();
    static readonly AsyncLocal<int> _untrackedCounter = new();
    static readonly ISubject<IReadOnlySignal> _signalsRequested = Subject.Synchronize(new Subject<IReadOnlySignal>());

    internal static IObservable<IReadOnlySignal> SignalsRequested() => Observable.Defer(() =>
    {
        var isCurrentContext = new AsyncLocal<bool>
        {
            Value = true
        };

        return _signalsRequested.Where(_ => isCurrentContext.Value);
    });

    public static T Untracked<T>(Func<T> action)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        lock (_untrackedLocker)
        {
            _untrackedCounter.Value++;
        }

        try
        {
            return action();
        }
        finally
        {
            lock (_untrackedLocker)
            {
                _untrackedCounter.Value--;
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
        bool shouldNotify;
        lock (_untrackedLocker)
        {
            shouldNotify = _untrackedCounter.Value == 0;
        }

        if (shouldNotify)
        {
            _signalsRequested.OnNext(property);
        } 

        return value;
    }
}