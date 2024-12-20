using System.Collections.Concurrent;
using System.ComponentModel;
using R3;

namespace SignalsDotnet;

public static partial class Signal
{
    static uint _nextComputedSignalAffinityValue;
    static readonly AsyncLocal<uint> _computedSignalAffinityValue = new();
    static readonly ConcurrentDictionary<uint, Subject<IReadOnlySignal>> _signalRequestedByComputedAffinity = new();
    internal static readonly PropertyChangedEventArgs PropertyChangedArgs = new("Value");

    internal static Observable<IReadOnlySignal> SignalsRequested()
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
            unchecked
            {
                _nextComputedSignalAffinityValue++;
            }
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

    internal static T GetValue<T>(IReadOnlySignal property, in T value)
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

    public class SignalsRequestedObservable : Observable<IReadOnlySignal>
    {
        protected override IDisposable SubscribeCore(Observer<IReadOnlySignal> observer)
        {
            lock (_computedSignalAffinityValue)
            {
                var affinityValue = _nextComputedSignalAffinityValue;

                unchecked
                {
                    _nextComputedSignalAffinityValue++;
                }

                _computedSignalAffinityValue.Value = affinityValue;
                var subject = new Subject<IReadOnlySignal>();
                _signalRequestedByComputedAffinity.TryAdd(affinityValue, subject);

                subject.Subscribe(observer.OnNext, observer.OnErrorResume, observer.OnCompleted);
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

