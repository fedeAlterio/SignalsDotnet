using System.ComponentModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;

namespace SignalsDotnet;

public abstract partial class Signal
{
    static readonly AsyncLocal<int> _untrackedCounter = new();
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

    internal static IDisposable ChangeComputedSignalAffinity()
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

    public static T Untracked<T>(Func<T> action)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        lock (_untrackedCounter)
        {
            _untrackedCounter.Value++;
        }

        try
        {
            return action();
        }
        finally
        {
            lock (_untrackedCounter)
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
        lock (_untrackedCounter)
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