using System.ComponentModel;
using System.Runtime.InteropServices;
using R3;

namespace SignalsDotnet;

public static partial class Signal
{
    static uint _nextComputedSignalAffinityValue;
    static readonly AsyncLocal<uint> _computedSignalAffinityValue = new();
    static readonly Dictionary<uint, Subject<IReadOnlySignal>> _signalRequestedByComputedAffinity = new();
    internal static readonly PropertyChangedEventArgs PropertyChangedArgs = new("Value");

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

    internal sealed class SignalsRequestedObservable : Observable<IReadOnlySignal>
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

                ref var subjectRef = ref CollectionsMarshal.GetValueRefOrAddDefault(_signalRequestedByComputedAffinity, affinityValue, out bool exists);
                if (exists)
                {
                    return Disposable.Empty;
                }

                var subject = new Subject<IReadOnlySignal>();
                subjectRef = subject;
                subject.Subscribe(observer.OnNext, observer.OnErrorResume, observer.OnCompleted);
                return new SignalsRequestedDisposable(affinityValue, subject);
            }
        }
    }

    readonly struct SignalsRequestedDisposable(uint affinityValue, Subject<IReadOnlySignal> subject) : IDisposable
    {
        public void Dispose()
        {
            lock (_computedSignalAffinityValue)
            {
                _signalRequestedByComputedAffinity.Remove(affinityValue, out _);
            }
            subject.Dispose();
        }
    }

    public readonly struct UntrackedReleaserDisposable(uint oldValue) : IDisposable
    {
        public void Dispose()
        {
            lock (_computedSignalAffinityValue)
            {
                _computedSignalAffinityValue.Value = oldValue;
            }
        }
    }
}

