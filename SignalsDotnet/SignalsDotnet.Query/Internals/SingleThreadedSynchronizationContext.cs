using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace SignalsDotnet.Query.Internals;

sealed class SingleThreadedSynchronizationContext : SynchronizationContext
{
    readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    readonly WaitCallback _pump;
    readonly Action<Exception> _onError;
    int _pending;

    SingleThreadedSynchronizationContext(Action<Exception> onError)
    {
        _pump = Pump;
        _onError = onError;
    }

    public static SynchronizationContext Create(Action<Exception> onError)
    {
        if (onError is null)
            throw new ArgumentNullException(nameof(onError));

        // Here we are making the implicit assumption that every custom implementation of SC
        // is single threaded. Tipically this is the case (unity, wpf, avalonia, maui etc)
        // but that's not sure in general. we should detect platform here to be precise..

        return Current ?? new SingleThreadedSynchronizationContext(onError);
    }

    public override void Post(SendOrPostCallback callback, object? state)
    {
        if (callback is null)
            throw new ArgumentNullException(nameof(callback));

        _queue.Enqueue((callback, state));

        if (Interlocked.Increment(ref _pending) == 1)
            ThreadPool.UnsafeQueueUserWorkItem(_pump, null);
    }

    public override void Send(SendOrPostCallback callback, object? state)
    {
        if (callback is null)
            throw new ArgumentNullException(nameof(callback));

        if (Current == this)
        {
            callback(state);
            return;
        }

        using var completed = new ManualResetEventSlim(false);
        ExceptionDispatchInfo? failure = null;

        Post(_ =>
        {
            try
            {
                callback(state);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                completed.Set();
            }
        }, null);

        completed.Wait();
        failure?.Throw();
    }

    public override SynchronizationContext CreateCopy() => this;

    void Pump(object? _)
    {
        var previous = Current;
        SetSynchronizationContext(this);

        try
        {
            var remaining = 1;

            while (remaining > 0)
            {
                var processed = 0;

                while (processed < remaining && _queue.TryDequeue(out var work))
                {
                    processed++;

                    try
                    {
                        work.Callback(work.State);
                    }
                    catch (Exception exception)
                    {
                        try
                        {
                            _onError(exception);
                        }
                        catch (Exception handlerFailure)
                        {
                            ThreadPool.UnsafeQueueUserWorkItem(static state => ((ExceptionDispatchInfo)state!).Throw(),
                                                               ExceptionDispatchInfo.Capture(handlerFailure));
                        }
                    }
                }

                remaining = Interlocked.Add(ref _pending, -processed);
            }
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }
}
