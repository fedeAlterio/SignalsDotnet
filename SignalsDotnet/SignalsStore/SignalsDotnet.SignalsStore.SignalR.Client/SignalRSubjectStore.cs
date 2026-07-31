using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using R3Async;
using R3Async.Subjects;

namespace SignalsDotnet.SignalsStore.SignalR.Client;

public sealed record SignalRSubjectStoreOptions
{
    public JsonSerializerOptions? SerializerOptions { get; init; }
}

public sealed class SignalRSubjectStore(Func<CancellationToken, ValueTask<HubConnection>> connectionFactory, SignalRSubjectStoreOptions? options = null) : ISubjectStore
{
    readonly SignalRSubjectStoreOptions _options = options ?? new SignalRSubjectStoreOptions();

    readonly RefCountLazy<HubConnection> _connection = new(async cancellationToken =>
    {
        var connection = await connectionFactory(cancellationToken);
        return new AsyncDisposableValue<HubConnection>
        {
            Value = connection,
            Disposable = AsyncDisposable.Create(async () =>
            {
                await using (connection)
                {
                    await connection.StopAsync();
                }
            })
        };
    });

    /// <summary>
    /// Creates an <see cref="ISubjectStore"/> backed by a <see cref="HubConnection"/> for
    /// <paramref name="url"/> (optionally customized via <paramref name="configureConnection"/>).
    /// Nothing is connected yet - the connection is built and started lazily on first use.
    /// </summary>
    public static SignalRSubjectStore Create(string url,
                                             Action<IHubConnectionBuilder>? configureConnection = null,
                                             SignalRSubjectStoreOptions? options = null)
    {
        if (string.IsNullOrEmpty(url))
            throw new ArgumentException("The url must be a non empty string.", nameof(url));

        return new SignalRSubjectStore(async cancellationToken =>
        {
            var builder = new HubConnectionBuilder().WithUrl(url);
            configureConnection?.Invoke(builder);

            var connection = builder.Build();

            try
            {
                await connection.StartAsync(cancellationToken);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }

            return connection;
        }, options);
    }

    public ISubject<T> CreateSubject<T>(string id)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("The id must be a non empty string.", nameof(id));

        return new SignalRSubject<T>(_connection, id, _options);
    }

    sealed class SignalRSubject<T>(RefCountLazy<HubConnection> connection, string id, SignalRSubjectStoreOptions options) : ISubject<T>
    {
        public AsyncObservable<T> Values { get; } = new SignalRValues<T>(connection, id, options);

        public async ValueTask OnNextAsync(T value, CancellationToken cancellationToken)
        {
            await using var connectionRef = await connection.GetAsync(cancellationToken);

            var json = JsonSerializer.Serialize(value, options.SerializerOptions);
            await connectionRef.Value.InvokeAsync("PublishValue", id, json, cancellationToken: cancellationToken);
        }

        public async ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
        {
            await using var connectionRef = await connection.GetAsync(cancellationToken);

            await connectionRef.Value.InvokeAsync("PublishError", id, error.Message, cancellationToken: cancellationToken);
        }

        public async ValueTask OnCompletedAsync(Result result)
        {
            await using var connectionRef = await connection.GetAsync(CancellationToken.None);

            await connectionRef.Value.InvokeAsync("PublishCompleted", id, result.IsSuccess, result.IsFailure ? result.Exception.Message : null);
        }
    }

    sealed class SignalRValues<T>(RefCountLazy<HubConnection> connection, string id, SignalRSubjectStoreOptions options) : AsyncObservable<T>
    {
        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var connectionRef = await connection.GetAsync(cancellationToken);
            HubConnection hubConnection;
            try
            {
                hubConnection = connectionRef.Value;
            }
            catch
            {
                await connectionRef.DisposeAsync();
                throw;
            }

            var subscriptionCts = new CancellationTokenSource();
            var stream = hubConnection.StreamAsync<SubjectNotification>("Subscribe", id, subscriptionCts.Token);
            var enumerator = stream.GetAsyncEnumerator(subscriptionCts.Token);

            bool hasFirst;
            try
            {
                hasFirst = await enumerator.MoveNextAsync();
            }
            catch
            {
                subscriptionCts.Dispose();
                await connectionRef.DisposeAsync();
                throw;
            }

            // The hub always yields a subscription ack (or failure) as its first item before any
            // Value/Error/Completed traffic, so the caller only ever resolves once the underlying
            // subject.Values.SubscribeAsync has genuinely completed server-side.
            if (!hasFirst || enumerator.Current is not { IsSubscriptionAck: true } ack)
            {
                subscriptionCts.Cancel();
                subscriptionCts.Dispose();
                await connectionRef.DisposeAsync();
                throw new SignalRSubjectException($"Subscribing to '{id}' did not receive an acknowledgement.");
            }

            if (ack.ErrorMessage is { } subscribeError)
            {
                subscriptionCts.Cancel();
                subscriptionCts.Dispose();
                await connectionRef.DisposeAsync();
                throw new SignalRSubjectException(subscribeError);
            }

            var reentrant = new AsyncLocal<bool>
            {
                Value = true
            };
            var pumpTask = PumpAsync(enumerator, observer, subscriptionCts.Token);

            return AsyncDisposable.Create(async () =>
            {
                subscriptionCts.Cancel();
                try
                {
                    if (!reentrant.Value)
                    {
                        await pumpTask;
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    await enumerator.DisposeAsync();
                    subscriptionCts.Dispose();
                    await connectionRef.DisposeAsync();
                }
            });
        }

        async Task PumpAsync(IAsyncEnumerator<SubjectNotification> enumerator, AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            try
            {
                while (await enumerator.MoveNextAsync())
                    await ForwardAsync(enumerator.Current, observer, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                await observer.OnCompletedAsync(Result.Failure(error));
            }
        }

        async ValueTask ForwardAsync(SubjectNotification notification, AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            switch (notification)
            {
                case { IsCompleted: true, IsCompletedSuccessfully: true }:
                    await observer.OnCompletedAsync(Result.Success);
                    break;

                case { IsCompleted: true, ErrorMessage: { } message }:
                    await observer.OnCompletedAsync(Result.Failure(new SignalRSubjectException(message)));
                    break;

                case { ErrorMessage: { } message }:
                    await observer.OnErrorResumeAsync(new SignalRSubjectException(message), cancellationToken);
                    break;

                case { ValueJson: { } valueJson }:
                    var value = JsonSerializer.Deserialize<T>(valueJson, options.SerializerOptions)!;
                    await observer.OnNextAsync(value, cancellationToken);
                    break;
            }
        }
    }
}
