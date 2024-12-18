using System.Reactive.Disposables;

namespace SignalsDotnet.Internals.Helpers;

internal static class ObservableEx
{
    public static IObservable<T> FromAsyncUsingAsyncContext<T>(Func<CancellationToken, ValueTask<T>> asyncAction)
    {
        return new FromAsyncContextObservable<T>(asyncAction);
    }

    public readonly struct FromAsyncContextObservable<T> : IObservable<T>
    {
        readonly Func<CancellationToken, ValueTask<T>> _asyncAction;

        public FromAsyncContextObservable(Func<CancellationToken, ValueTask<T>> asyncAction)
        {
            _asyncAction = asyncAction;
        }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            var disposable = new CancellationDisposable();
            var token = disposable.Token;

            try
            {
                var task = _asyncAction(token);
                if (task.IsCompleted)
                {
                    observer.OnNext(task.GetAwaiter().GetResult());
                    observer.OnCompleted();
                    return disposable;
                }

                BindObserverToTask(task, observer);
            }
            catch (Exception e)
            {
                observer.OnError(e);
            }

            return disposable;
        }

        static async void BindObserverToTask(ValueTask<T> task, IObserver<T> observer)
        {
            try
            {
                var result = await task;
                observer.OnNext(result);
                observer.OnCompleted();
            }
            catch (Exception e)
            {
                try
                {
                    observer.OnError(e);
                }
                catch
                {
                    // Ignored
                }
            }
        }
    }
}
