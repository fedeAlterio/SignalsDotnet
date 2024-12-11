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
}
