using SignalsDotnet.Query.Internals;

namespace SignalsDotnet.Query;

public static class SignalIsland
{
    public static Action<Exception> UnhandledError { get; set; } = LogToConsole;

    static void LogToConsole(Exception exception) =>
        Console.Error.WriteLine($"Unhandled exception in {nameof(SignalIsland)}: {exception}");
}

public sealed class SignalIsland<T>
{
    readonly Func<CancellationToken, ValueTask<T>> _factory;
    readonly object _gate = new();

    TaskCompletionSource<T>? _value;

    public SynchronizationContext Context { get; }

    public SignalIsland(Func<CancellationToken, ValueTask<T>> factory)
    {
        if (factory is null)
            throw new ArgumentNullException(nameof(factory));

        _factory = factory;
        Context = SingleThreadedSynchronizationContext.Create(OnUnhandledError);
    }

    public ValueTask InvokeAsync(Func<T, ValueTask> action, CancellationToken cancellationToken = default)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        return InvokeCoreAsync(action, cancellationToken);
    }

    public ValueTask InvokeAsync(Action<T> action, CancellationToken cancellationToken = default)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        return InvokeCoreAsync(action, cancellationToken);
    }

    public ValueTask<TResult> InvokeAsync<TResult>(Func<T, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        return InvokeCoreAsync(action, cancellationToken);
    }

    public ValueTask<TResult> InvokeAsync<TResult>(Func<T, TResult> action, CancellationToken cancellationToken = default)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        return InvokeCoreAsync(action, cancellationToken);
    }

    ValueTask InvokeCoreAsync(Func<T, ValueTask> action, CancellationToken cancellationToken) =>
        Context.InvokeAsync(async token => await action(await ValueAsync(token)), cancellationToken);

    ValueTask InvokeCoreAsync(Action<T> action, CancellationToken cancellationToken) =>
        Context.InvokeAsync(async token => action(await ValueAsync(token)), cancellationToken);

    ValueTask<TResult> InvokeCoreAsync<TResult>(Func<T, ValueTask<TResult>> action, CancellationToken cancellationToken) =>
        Context.InvokeAsync(async token => await action(await ValueAsync(token)), cancellationToken);

    ValueTask<TResult> InvokeCoreAsync<TResult>(Func<T, TResult> action, CancellationToken cancellationToken) =>
        Context.InvokeAsync(async token => action(await ValueAsync(token)), cancellationToken);

    public IAwaitable<T> SwitchToIslandContextAsync(CancellationToken cancellationToken = default) =>
        new IslandContextAwaitable(this, cancellationToken);

    readonly struct IslandContextAwaitable(SignalIsland<T> island, CancellationToken cancellationToken) : IAwaitable<T>
    {
        public IAwaiter<T> GetAwaiter() => new IslandContextAwaiter(island, cancellationToken);
    }

    sealed class IslandContextAwaiter : IAwaiter<T>
    {
        readonly SignalIsland<T> _island;
        readonly CancellationToken _cancellationToken;
        Task<T>? _resolved;

        public IslandContextAwaiter(SignalIsland<T> island, CancellationToken cancellationToken)
        {
            _island = island;
            _cancellationToken = cancellationToken;
        }

        public bool IsCompleted
        {
            get
            {
                if (SynchronizationContext.Current != _island.Context || _cancellationToken.IsCancellationRequested)
                    return false;

                if (_resolved is not null)
                    return true;

                var value = _island.ValueAsync(_cancellationToken);

                if (!value.IsCompletedSuccessfully)
                    return false;

                _resolved = Task.FromResult(value.Result);
                return true;
            }
        }

        public void OnCompleted(Action continuation)
        {
            _island.Context.Post(async void (_) =>
            {
                try
                {
                    try
                    {
                        _resolved = Task.FromResult(await _island.ValueAsync(_cancellationToken));
                    }
                    catch (Exception exception)
                    {
                        _resolved = Task.FromException<T>(exception);
                    }

                    continuation();
                }
                catch (Exception exception)
                {
                    OnUnhandledError(exception);
                }
            }, null);
        }

        public T GetResult()
        {
            _cancellationToken.ThrowIfCancellationRequested();

            _resolved ??= _island.ValueAsync(_cancellationToken).AsTask();

            return _resolved.GetAwaiter().GetResult();
        }
    }

    ValueTask<T> ValueAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<T> value;
        bool owned;

        lock (_gate)
        {
            owned = _value is null;
            value = _value ??= new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        if (owned)
            Bind(value, cancellationToken);

        var task = value.Task;

        return task.IsCompletedSuccessfully
            ? new ValueTask<T>(task.Result)
            : new ValueTask<T>(task);
    }

    async void Bind(TaskCompletionSource<T> value, CancellationToken cancellationToken)
    {
        try
        {
            value.TrySetResult(await _factory(cancellationToken));
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
        {
            Reset(value);
            value.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            Reset(value);
            value.TrySetException(exception);
        }
    }

    void Reset(TaskCompletionSource<T> value)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_value, value))
                _value = null;
        }
    }

    static void OnUnhandledError(Exception exception) => SignalIsland.UnhandledError?.Invoke(exception);
}
