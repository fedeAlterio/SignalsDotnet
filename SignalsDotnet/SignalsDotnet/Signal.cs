using R3;
using SignalsDotnet.Internals;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SignalsDotnet;

public static partial class Signal
{
    public record StackState
    {
        public Subject<INotifySignalChanged>? Subject { get; set; }
    }
    static readonly AsyncLocal<Stack<StackState>?> _signalsStack = new();
    internal static readonly PropertyChangedEventArgs PropertyChangedArgs = new("Value");

    internal static SignalsRequestedObservable SignalsRequested()
    {
        return new SignalsRequestedObservable();
    }

    public static UntrackedReleaserDisposable UntrackedScope()
    {
        lock (_signalsStack)
        {
            var stack = _signalsStack.Value;
            if (stack is null || !stack.TryPeek(out var state) || state.Subject is null)
            {
                return new(null);
            }

            stack.Push(new());
            return new UntrackedReleaserDisposable(stack);
        }
    }

    internal static bool InsideComputed => _signalsStack.Value is { Count: > 0 };

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
        StackState state;
        lock (_signalsStack)
        {
            if (_signalsStack.Value?.TryPeek(out state!) is not true)
            {
                return;
            }
        }

        state.Subject?.OnNext(signal);
    }

    internal sealed class SignalsRequestedObservable : Observable<INotifySignalChanged>
    {
        protected override IDisposable SubscribeCore(Observer<INotifySignalChanged> observer)
        {
            lock (_signalsStack)
            {
                bool shouldClear;
                Subject<INotifySignalChanged> subject;
                var stack = _signalsStack.Value ??= new();

                if (stack.Count == 0)
                {
                    subject = new();
                    stack.Push(new StackState
                    {
                        Subject = subject
                    });
                    shouldClear = true;
                }
                else
                {
                    stack.TryPeek(out var state);
                    subject = state!.Subject ??= new();
                    shouldClear = false;
                }

                subject.Subscribe(observer.OnNext, observer.OnErrorResume, observer.OnCompleted);
                return new SignalsRequestedDisposable(shouldClear);
            }
        }
    }

    readonly struct SignalsRequestedDisposable(bool shouldClear) : IDisposable
    {
        public void Dispose()
        {
            if (!shouldClear) return;
            lock (_signalsStack)
            {
                _signalsStack.Value = null;
            }
        }
    }

    public readonly struct UntrackedReleaserDisposable(Stack<StackState>? stack) : IDisposable
    {
        public void Dispose()
        {
            if (stack is null) return;
            lock (_signalsStack)
            {
                stack.Pop();
            }
        }
    }

    public static IDisposable TrackedScope(out IDisposable trackingSubscription, Action onSignalChanged)
    {
        int anySignalArrived = 0;
        var signalChangeSubscription = new CompositeDisposable();
        var signalsRequested = new HashSet<INotifySignalChanged>(ReferenceEqualityComparer<INotifySignalChanged>.Instance);
        var subscription = SignalsRequested()
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
