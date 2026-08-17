namespace SignalsDotnet.Query.Internals;

static class SynchronizationContextExtensions
{
    internal static async ValueTask InvokeAsync(this SynchronizationContext context, Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
    {
        await context.InvokeAsync<object?>(async token =>
        {
            await action(token);
            return null;
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask InvokeAsync(this SynchronizationContext context, Action<CancellationToken> action, CancellationToken cancellationToken = default)
    {
        await context.InvokeAsync<object?>(token =>
        {
            action(token);
            return new ValueTask<object?>(default(object?));
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask<TResult> InvokeAsync<TResult>(this SynchronizationContext context, Func<CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(static state => ((TaskCompletionSource<TResult>)state!).TrySetCanceled(), completion)
            : default;

        context.Post(async void (_) =>
        {
            try
            {
                completion.TrySetResult(await action(cancellationToken));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }, null);

        return await completion.Task.ConfigureAwait(false);
    }
}
