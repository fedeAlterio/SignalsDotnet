using R3;
using SignalsDotnet.Internals;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SignalsDotnet;

public static partial class Signal
{
    internal sealed class Scope : IDisposable
    {
        public static readonly Scope Untracked = new(null, null);

        readonly Scope? _previous;
        Observer<INotifySignalChanged>? _observer;

        public Scope(Observer<INotifySignalChanged>? observer, Scope? previous)
        {
            _observer = observer;
            _previous = previous;
        }

        public bool IsTracking => Volatile.Read(ref _observer) is not null;

        public void Notify(INotifySignalChanged signal) => Volatile.Read(ref _observer)?.OnNext(signal);

        public void Dispose()
        {
            Volatile.Write(ref _observer, null);
            if (ReferenceEquals(_currentScope.Value, this))
                _currentScope.Value = _previous;
        }
    }

    static readonly AsyncLocal<Scope?> _currentScope = new();
    internal static readonly PropertyChangedEventArgs PropertyChangedArgs = new("Value");

    internal static readonly SignalsRequestedObservable SignalsRequested = new();

    public static UntrackedReleaserDisposable UntrackedScope()
    {
        var current = _currentScope.Value;

        // Only shadow when we're actively tracking; if there's no scope, or the current
        // scope is already not tracking, there's nothing to suppress.
        if (current is not { IsTracking: true }) return new(null);

        _currentScope.Value = Scope.Untracked;
        return new UntrackedReleaserDisposable(current);
    }

    /// <summary>
    /// Whether a computation is currently collecting dependencies. Lets external reactive
    /// sources skip creating tracking state for reads that nothing is observing.
    /// </summary>
    public static bool InsideComputed => _currentScope.Value is { IsTracking: true };

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static T GetValue<T>(INotifySignalChanged signal, in T value)
    {
        NotifySignalRequested(signal);
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void NotifySignalRequested(INotifySignalChanged signal)
    {
        // Lock-free: _currentScope is per-flow, so this is a plain read.
        _currentScope.Value?.Notify(signal);
    }

    internal sealed class SignalsRequestedObservable : Observable<INotifySignalChanged>
    {
        protected override IDisposable SubscribeCore(Observer<INotifySignalChanged> observer)
        {
            var scope = new Scope(observer, _currentScope.Value);
            _currentScope.Value = scope;
            return scope;
        }
    }

    public readonly struct UntrackedReleaserDisposable : IDisposable
    {
        readonly Scope? _restoreTo;
        internal UntrackedReleaserDisposable(Scope? restoreTo) => _restoreTo = restoreTo;

        public void Dispose()
        {
            if (_restoreTo is null) return;
            _currentScope.Value = _restoreTo;
        }
    }

    public static IDisposable TrackedScope(out IDisposable trackingSubscription, Action onSignalChanged)
    {
        int anySignalArrived = 0;
        var signalChangeSubscription = new CompositeDisposable();
        var signalsRequested = new HashSet<INotifySignalChanged>(ReferenceEqualityComparer<INotifySignalChanged>.Instance);
        var subscription = SignalsRequested
            .Subscribe(signal =>
            {
                if (!signalsRequested.Add(signal)) return;

                signal.FutureValues.Subscribe(_ =>
                {
                    if (Interlocked.CompareExchange(ref anySignalArrived, 1, 0) == 1) return;

                    signalChangeSubscription.Dispose();
                    onSignalChanged();
                }).AddTo(signalChangeSubscription);
            });

        trackingSubscription = signalChangeSubscription;
        return subscription;
    }
}
