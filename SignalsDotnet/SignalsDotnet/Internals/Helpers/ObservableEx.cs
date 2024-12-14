using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace SignalsDotnet.Internals.Helpers;

internal static class ObservableEx
{
    public static IObservable<T> FromAsyncUsingAsyncContext<T>(Func<CancellationToken, Task<T>> asyncAction)
    {
        if (asyncAction is null)
            throw new ArgumentNullException(nameof(asyncAction));

        return Observable.Create<T>(observer =>
        {
            var disposable = new CancellationDisposable();
            var token = disposable.Token;

            try
            {
                var task = asyncAction(token);
                if (task.IsFaulted)
                {
                    var exception = task.GetSingleException();
                    observer.OnError(exception);
                    return disposable;
                }

                BindObserverToTask(task, observer);
            }
            catch (Exception e)
            {
                observer.OnError(e);
            }

            return disposable;
        });

        async void BindObserverToTask(Task<T> task, IObserver<T> observer)
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

    static Exception GetSingleException(this Task t)
    {
        Debug.Assert(t is { IsFaulted: true, Exception: not null });

        if (t.Exception!.InnerException != null)
        {
            return t.Exception.InnerException;
        }

        return t.Exception;
    }
}
