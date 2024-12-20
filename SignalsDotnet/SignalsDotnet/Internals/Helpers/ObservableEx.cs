using R3;

namespace SignalsDotnet.Internals.Helpers;

internal static class ObservableEx
{
    public static FromAsyncContextObservable<T> FromAsyncUsingAsyncContext<T>(Func<CancellationToken, ValueTask<T>> asyncAction)
    {
        return new FromAsyncContextObservable<T>(asyncAction);
    }

    public class FromAsyncContextObservable<T> : Observable<T>
    {
        readonly Func<CancellationToken, ValueTask<T>> _asyncAction;

        public FromAsyncContextObservable(Func<CancellationToken, ValueTask<T>> asyncAction)
        {
            _asyncAction = asyncAction;
        }

        protected override IDisposable SubscribeCore(Observer<T> observer)
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
                observer.OnCompleted(e);
            }

            return disposable;
        }

        static async void BindObserverToTask(ValueTask<T> task, Observer<T> observer)
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
                    observer.OnCompleted(e);
                }
                catch
                {
                    // Ignored
                }
            }
        }
    }
}
