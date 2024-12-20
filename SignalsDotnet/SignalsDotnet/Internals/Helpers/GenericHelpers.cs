namespace SignalsDotnet.Internals.Helpers;

internal static class GenericHelpers
{
    public static Func<CancellationToken, ValueTask<T>> ToAsyncValueTask<T>(this Func<T> func)
    {
        if (func is null)
            throw new ArgumentNullException(nameof(func));

        return token =>
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(func());
        };
    }

    public static Func<CancellationToken, ValueTask<T>> TraceWhenExecuting<T>(this Func<CancellationToken, ValueTask<T>> func, out IReadOnlySignal<bool> isExecuting)
    {
        var isExecutingSignal = new Signal<bool>();
        isExecuting = isExecutingSignal;

        return async token =>
        {
            try
            {
                isExecutingSignal.Value = true;
                return await func(token);
            }
            finally
            {
                isExecutingSignal.Value = false;
            }
        };
    }
}
