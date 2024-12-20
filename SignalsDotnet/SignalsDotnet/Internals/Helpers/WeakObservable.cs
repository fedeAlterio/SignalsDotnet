using System.Reflection;
using R3;

namespace SignalsDotnet.Internals.Helpers;

internal static class WeakObservable
{
    public static IDisposable SubscribeWeakly<T>(this Observable<T> source, Action<T> onNext)
    {
        var weakObserver = new WeakAction<T>(onNext);
        var subscription = source.Subscribe(weakObserver.OnNext);
        weakObserver.GarbageCollected += subscription.Dispose;
        return subscription;
    }

    class WeakAction<T>
    {
        public event Action? GarbageCollected;
        readonly WeakReference<object>? _weakTarget;
        readonly MethodInfo _method;
        readonly bool _isStatic;

        public WeakAction(Action<T> onNext)
        {
            var target = onNext.Target;
            _weakTarget = target is not null ? new(target) : null;
            _isStatic = target is null;
            _method = onNext.Method;
        }

        public void OnNext(T? value)
        {
            if (TryGetTargetOrNotifyCollected(out var target))
            {
                _method.Invoke(target, new object?[] { value });
            }
        }

        bool TryGetTargetOrNotifyCollected(out object? target)
        {
            if (_isStatic)
            {
                target = null;
                return true;
            }

            var ret = _weakTarget!.TryGetTarget(out target);
            if (!ret)
            {
                GarbageCollected?.Invoke();
            }

            return ret;
        }
    }
}