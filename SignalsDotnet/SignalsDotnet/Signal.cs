using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;

namespace SignalsDotnet;

public abstract partial class Signal
{
    static uint _nextComputedSignalAffinityValue;
    static readonly AsyncLocal<uint> _computedSignalAffinityValue = new();
    static readonly ConcurrentDictionary<uint, Subject<IReadOnlySignal>> _signalRequestedByComputedAffinity = new();

    internal static SignalsRequestedObservable SignalsRequested()
    {
        return new SignalsRequestedObservable();
    }

    public static UntrackedReleaserDisposable UntrackedScope()
    {
        uint oldAffinity;
        lock (_computedSignalAffinityValue)
        {
            oldAffinity = _computedSignalAffinityValue.Value;
            _computedSignalAffinityValue.Value = _nextComputedSignalAffinityValue;
            var currentValue = _nextComputedSignalAffinityValue;
            _nextComputedSignalAffinityValue = currentValue == uint.MaxValue ? 0 : currentValue + 1;
        }

        return new UntrackedReleaserDisposable(oldAffinity);
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
        uint affinityValue;
        lock (_computedSignalAffinityValue)
        {
            affinityValue = _computedSignalAffinityValue.Value;
        }

        if (_signalRequestedByComputedAffinity.TryGetValue(affinityValue, out var subject))
        {
            subject.OnNext(property);
        }

        return value;
    }


    public readonly struct SignalsRequestedObservable : IObservable<IReadOnlySignal>
    {
        public IDisposable Subscribe(IObserver<IReadOnlySignal> observer)
        {
            lock (_computedSignalAffinityValue)
            {
                var affinityValue = _nextComputedSignalAffinityValue;

                var currentValue = _nextComputedSignalAffinityValue;
                _nextComputedSignalAffinityValue = currentValue == uint.MaxValue ? 0 : currentValue + 1;

                _computedSignalAffinityValue.Value = affinityValue;
                var subject = new Subject<IReadOnlySignal>();
                _signalRequestedByComputedAffinity.TryAdd(affinityValue, subject);

                subject.Subscribe(observer);
                return new SignalsRequestedDisposable(affinityValue, subject);
            }
        }
    }

    readonly struct SignalsRequestedDisposable : IDisposable
    {
        readonly uint _affinityValue;
        readonly Subject<IReadOnlySignal> _subject;

        public SignalsRequestedDisposable(uint affinityValue, Subject<IReadOnlySignal> subject)
        {
            _affinityValue = affinityValue;
            _subject = subject;
        }
        public void Dispose()
        {
            _signalRequestedByComputedAffinity.TryRemove(_affinityValue, out _);
            _subject.Dispose();
        }
    }

    public readonly struct UntrackedReleaserDisposable : IDisposable
    {
        readonly uint _oldValue;

        public UntrackedReleaserDisposable(uint oldValue)
        {
            _oldValue = oldValue;
        }
        public void Dispose()
        {
            lock (_computedSignalAffinityValue)
            {
                _computedSignalAffinityValue.Value = _oldValue;
            }
        }
    }
}

