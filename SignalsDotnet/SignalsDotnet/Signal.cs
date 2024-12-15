using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;

namespace SignalsDotnet;

public abstract partial class Signal
{
    static readonly ISubject<IReadOnlySignal> _signalsRequested = Subject.Synchronize(new Subject<IReadOnlySignal>());
    static readonly AsyncLocal<uint> _computedSignalAffinityIndex = new();

    internal static IObservable<IReadOnlySignal> SignalsRequested() => Observable.Defer(() =>
    {
        var isSameContext = new AsyncLocal<bool>();
        isSameContext.Value = true;
        uint startSignalRecursion;
        lock (_computedSignalAffinityIndex)
        {
            startSignalRecursion = _computedSignalAffinityIndex.Value;
        }

        return _signalsRequested.Where(_ =>
        {
            if (!isSameContext.Value)
            {
                return false;
            }

            lock (_computedSignalAffinityIndex)
            {
                return _computedSignalAffinityIndex.Value == startSignalRecursion;
            }
        });
    });

    public static IDisposable UntrackedScope()
    {
        lock (_computedSignalAffinityIndex)
        {
            _computedSignalAffinityIndex.Value++;
        }

        return Disposable.Create(() =>
        {
            lock (_computedSignalAffinityIndex)
            {
                _computedSignalAffinityIndex.Value--;
            }
        });
    }

    public static async Task<T> Untracked<T>(Func<Task<T>> action)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        using (UntrackedScope())
        {
            return await action();
        }
    }
    public static async Task Untracked(Func<Task> action)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        using (UntrackedScope())
        {
            await action();
        }
    }

    public static T Untracked<T>(Func<T> action)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        using (UntrackedScope())
        {
            return action();
        }
    }

    public static void Untracked(Action action)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        using (UntrackedScope())
        {
            action();
        }
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

        using (UntrackedScope())
        {
            field = value;
            OnPropertyChanged(propertyName);
        }
    }


    protected static T GetValue<T>(IReadOnlySignal property, in T value)
    {
        _signalsRequested.OnNext(property);

        return value;
    }
}