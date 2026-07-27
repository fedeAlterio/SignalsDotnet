using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using R3Async;
using R3Async.Subjects;
using StackExchange.Redis;

namespace SignalsDotnet.SignalsStore.Redis;

public sealed record RedisReplayLatestSubjectStoreOptions
{
    public string Namespace { get; init; } = "signals";

    public JsonSerializerOptions? SerializerOptions { get; init; }
}

public sealed class RedisSubjectException(string message) : Exception(message);

public sealed class RedisReplayLatestSubjectStore : ISubjectStore
{
    readonly IConnectionMultiplexer _connection;
    readonly RedisReplayLatestSubjectStoreOptions _options;

    public RedisReplayLatestSubjectStore(IConnectionMultiplexer connection, RedisReplayLatestSubjectStoreOptions? options = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? new RedisReplayLatestSubjectStoreOptions();
    }

    public ISubject<T> CreateSubject<T>(string id)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("The id must be a non empty string.", nameof(id));

        return new RedisReplayLatestSubject<T>(_connection, $"{_options.Namespace}:{id}", _options);
    }

    sealed record Notification<T>
    {
        public T? Value { get; init; }
        public string? ErrorMessage { get; init; }
        public bool IsCompleted { get; init; }
        public bool IsCompletedSuccessfully { get; init; }

        public static Notification<T> ForValue(T value) => new() { Value = value };

        public static Notification<T> ForError(Exception error) => new() { ErrorMessage = error.Message };

        public static Notification<T> ForCompletion(Result result) => new()
        {
            IsCompleted = true,
            IsCompletedSuccessfully = result.IsSuccess,
            ErrorMessage = result.IsFailure ? result.Exception.Message : null,
        };

        public ValueTask ForwardTo(AsyncObserver<T> observer, CancellationToken cancellationToken) => this switch
        {
            { IsCompleted: true, IsCompletedSuccessfully: true } =>
                observer.OnCompletedAsync(Result.Success),
            { IsCompleted: true, ErrorMessage: { } message } =>
                observer.OnCompletedAsync(Result.Failure(new RedisSubjectException(message))),
            { ErrorMessage: { } message } =>
                observer.OnErrorResumeAsync(new RedisSubjectException(message), cancellationToken),
            _ => observer.OnNextAsync(Value!, cancellationToken),
        };
    }

    sealed class RedisReplayLatestSubject<T> : ISubject<T>
    {
        readonly IConnectionMultiplexer _connection;
        readonly string _key;
        readonly RedisReplayLatestSubjectStoreOptions _options;

        const string PublishScript = """
            local current = redis.call('GET', KEYS[1])
            if current and cjson.decode(current).IsCompleted then
                return -1
            end

            local version = redis.call('INCR', KEYS[2])
            redis.call('SET', KEYS[1], ARGV[1])
            redis.call('PUBLISH', KEYS[3], version .. '|' .. ARGV[1])
            return version
            """;

        public RedisReplayLatestSubject(IConnectionMultiplexer connection, string key, RedisReplayLatestSubjectStoreOptions options)
        {
            _connection = connection;
            _key = key;
            _options = options;
            Values = new RedisValues<T>(connection, key, options)
                .TakeUntil(ConnectionFailedSignal(connection, key), new TakeUntilOptions { SourceFailsWhenOtherFails = true });
        }

        static CompletionObservableDelegate ConnectionFailedSignal(IConnectionMultiplexer connection, string key) => notifyStop =>
        {
            EventHandler<ConnectionFailedEventArgs> onConnectionFailed = (_, e) =>
                notifyStop(Result.Failure(e.Exception ?? new RedisSubjectException($"Redis connection failed for '{key}'.")));

            connection.ConnectionFailed += onConnectionFailed;

            return AsyncDisposable.Create(() => connection.ConnectionFailed -= onConnectionFailed);
        };

        public AsyncObservable<T> Values { get; }

        string VersionKey => $"{_key}:version";

        public async ValueTask OnNextAsync(T value, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(Notification<T>.ForValue(value), _options.SerializerOptions);
            await PublishAsync(json);
        }

        public async ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(Notification<T>.ForError(error), _options.SerializerOptions);
            await _connection.GetSubscriber().PublishAsync(RedisChannel.Literal(_key), "|" + json);
        }

        public async ValueTask OnCompletedAsync(Result result)
        {
            var json = JsonSerializer.Serialize(Notification<T>.ForCompletion(result), _options.SerializerOptions);
            await PublishAsync(json);
        }

        async ValueTask PublishAsync(string json)
        {
            var db = _connection.GetDatabase();
            await db.ScriptEvaluateAsync(PublishScript,
                                         [_key, VersionKey, RedisChannel.Literal(_key).ToString()],
                                         [json]);
        }
    }

    sealed class RedisValues<T>(IConnectionMultiplexer connection, string key, RedisReplayLatestSubjectStoreOptions options)
        : AsyncObservable<T>
    {
        string VersionKey => $"{key}:version";

        protected override async ValueTask<IAsyncDisposable> SubscribeAsyncCore(AsyncObserver<T> observer,
                                                                                CancellationToken cancellationToken)
        {
            var channel = RedisChannel.Literal(key);
            var subscriber = connection.GetSubscriber();
            var queue = await subscriber.SubscribeAsync(channel);

            var subscriptionCts = new CancellationTokenSource();
            var subscriptionToken = subscriptionCts.Token;

            var pending = Channel.CreateBounded<ChannelMessage>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = true,
            });

            var subscription = new Subscription(subscriber, queue, channel, subscriptionCts, pending.Writer);

            try
            {
                var db = connection.GetDatabase();
                var values = await db.StringGetAsync([key, VersionKey]);
                var current = values[0];
                var lastVersion = new StrongBox<long>(values[1].IsNull ? 0L : (long)values[1]);

                if (!current.IsNull)
                {
                    try
                    {
                        var notification = JsonSerializer.Deserialize<Notification<T>>((string)current!, options.SerializerOptions)!;
                        await notification.ForwardTo(observer, cancellationToken);
                    }
                    catch (JsonException error)
                    {
                        await observer.OnErrorResumeAsync(error, cancellationToken);
                    }
                }

                queue.OnMessage(message => pending.Writer.TryWrite(message));
                subscription.ConsumerTask = ConsumeAsync(pending.Reader, observer, lastVersion, subscriptionToken);
            }
            catch
            {
                await subscription.DisposeAsync();
                throw;
            }

            return subscription;
        }

        async Task ConsumeAsync(ChannelReader<ChannelMessage> reader, AsyncObserver<T> observer, StrongBox<long> lastVersion, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var message in reader.ReadAllAsync(cancellationToken))
                    await OnMessageAsync(message, observer, lastVersion, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        async Task OnMessageAsync(ChannelMessage message, AsyncObserver<T> observer, StrongBox<long> lastVersion, CancellationToken cancellationToken)
        {
            var raw = (string?)message.Message;
            if (raw is null)
                return;

            var span = raw.AsSpan();
            var separator = span.IndexOf('|');
            if (separator < 0)
            {
                await observer.OnErrorResumeAsync(
                    new RedisSubjectException($"Malformed notification for '{key}'."),
                    cancellationToken);

                return;
            }

            var versionPart = span[..separator];

            Notification<T> notification;
            try
            {
                notification = JsonSerializer.Deserialize<Notification<T>>(span[(separator + 1)..], options.SerializerOptions)!;
            }
            catch (JsonException error)
            {
                await observer.OnErrorResumeAsync(error, cancellationToken);
                return;
            }

            if (versionPart.Length > 0)
            {
                var version = long.Parse(versionPart);
                if (version <= Interlocked.Exchange(ref lastVersion.Value, version))
                    return;
            }

            await notification.ForwardTo(observer, cancellationToken);
        }

        sealed class Subscription(ISubscriber subscriber,
                                  ChannelMessageQueue queue,
                                  RedisChannel channel,
                                  CancellationTokenSource cts,
                                  ChannelWriter<ChannelMessage> pendingWriter)
            : IAsyncDisposable
        {
            int _disposed;

            public Task? ConsumerTask { get; set; }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                queue.Unsubscribe();
                await subscriber.UnsubscribeAsync(channel);

                cts.Cancel();
                pendingWriter.TryComplete();

                if (ConsumerTask is not null)
                {
                    try
                    {
                        await ConsumerTask;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }

                cts.Dispose();
            }
        }
    }
}
