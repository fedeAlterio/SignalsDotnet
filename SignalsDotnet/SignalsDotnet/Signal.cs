using R3;
using SignalsDotnet.Internals;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SignalsDotnet;

public static partial class Signal
{
    internal sealed class Scope
    {
        Subject<INotifySignalChanged>? _subject;

        public Scope(Scope? parent) => Parent = parent;
        public Scope? Parent { get; }

        public Subject<INotifySignalChanged>? Subject => _subject;

        public Subject<INotifySignalChanged> GetOrCreateSubject()
        {
            var existing = _subject;
            if (existing is not null) return existing;

            var created = new Subject<INotifySignalChanged>();
            return Interlocked.CompareExchange(ref _subject, created, null) ?? created;
        }
    }

    static readonly AsyncLocal<Scope?> _currentScope = new();
    internal static readonly PropertyChangedEventArgs PropertyChangedArgs = new("Value");

    internal static readonly SignalsRequestedObservable SignalsRequested = new();

    public static UntrackedReleaserDisposable UntrackedScope()
    {
        var current = _currentScope.Value;

        // Only shadow when we're actively tracking; if there's no scope, or the current
        // scope is already untracked (null Subject), there's nothing to suppress.
        if (current?.Subject is null) return new(null);

        _currentScope.Value = new Scope(current);
        return new UntrackedReleaserDisposable(current);
    }

    internal static bool InsideComputed => _currentScope.Value is not null;

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
        _currentScope.Value?.Subject?.OnNext(signal);
    }

    internal sealed class SignalsRequestedObservable : Observable<INotifySignalChanged>
    {
        protected override IDisposable SubscribeCore(Observer<INotifySignalChanged> observer)
        {
            var current = _currentScope.Value;
            bool shouldClear;
            Scope scope;

            if (current is null)
            {
                scope = new Scope(null);
                _currentScope.Value = scope;
                shouldClear = true;
            }
            else
            {
                scope = current;
                shouldClear = false;
            }

            var subject = scope.GetOrCreateSubject();
            subject.Subscribe(observer.OnNext, observer.OnErrorResume, observer.OnCompleted);
            return new SignalsRequestedDisposable(shouldClear);
        }
    }

    readonly struct SignalsRequestedDisposable(bool shouldClear) : IDisposable
    {
        public void Dispose()
        {
            if (shouldClear)
                _currentScope.Value = null;
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
