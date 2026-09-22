using SignalsDotnet.Internals;

namespace SignalsDotnet;

public partial class Signal
{
    public static BatchReleaserDisposable BatchScope()
    {
        Internals.BatchScope.Enter();
        return new BatchReleaserDisposable(true);
    }

    public static void Batch(Action action)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        using (BatchScope())
        {
            action();
        }
    }

    public static T Batch<T>(Func<T> action)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        using (BatchScope())
        {
            return action();
        }
    }

    [Obsolete("Batch is synchronous: the batch closes at the first await, so writes after it are not batched. Await the asynchronous work first, then batch the synchronous writes.", true)]
    public static void Batch(Func<Task> action) => throw new NotSupportedException();

    public readonly struct BatchReleaserDisposable : IDisposable
    {
        readonly bool _entered;
        internal BatchReleaserDisposable(bool entered) => _entered = entered;

        public void Dispose()
        {
            if (_entered)
                Internals.BatchScope.Exit();
        }
    }
}
