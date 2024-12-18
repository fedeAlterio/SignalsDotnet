using System.ComponentModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;

namespace SignalsDotnet;

public abstract partial class Signal
{
    static readonly ISubject<IReadOnlySignal> _signalsRequested = new Subject<IReadOnlySignal>();
    static readonly AsyncLocal<uint> _computedSignalAffinityIndex = new();
    internal static IObservable<IReadOnlySignal> SignalsRequested()
    {
        return Observable.Defer(() =>
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
    }

    public static UntrackedReleaserDisposable UntrackedScope()
    {
        lock (_computedSignalAffinityIndex)
        {
            _computedSignalAffinityIndex.Value++;
        }

        return new UntrackedReleaserDisposable();
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

    protected void SetValue<T>(ref T field,
                               T value,
                               IEqualityComparer<T> equalityComparer,
                               bool raiseOnlyWhenChanged,
                               [CallerMemberName] string? propertyName = null)
    {
        if (raiseOnlyWhenChanged && equalityComparer.Equals(field, value))
            return;
        field = value;

        var propertyChanged = PropertyChanged;
        if (propertyChanged is null)
        {
            return;
        }

        using (UntrackedScope())
        {
            propertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }


    protected static T GetValue<T>(IReadOnlySignal property, in T value)
    {
        _signalsRequested.OnNext(property);

        return value;
    }

    public readonly struct UntrackedReleaserDisposable : IDisposable
    {
        public void Dispose()
        {
            lock (_computedSignalAffinityIndex)
            {
                _computedSignalAffinityIndex.Value--;
            }
        }
    }
}

