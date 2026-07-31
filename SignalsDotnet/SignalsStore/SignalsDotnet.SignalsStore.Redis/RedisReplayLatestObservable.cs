using System.Text.Json;
using System.Threading.Channels;
using R3Async;
using StackExchange.Redis;

namespace SignalsDotnet.SignalsStore.Redis;

static class RedisReplayLatestObservable<T>
{
    public static AsyncObservable<T> Create(RefCountLazy<IConnectionMultiplexer> refCountConnection, string key, RedisReplayLatestSubjectStoreOptions options)
    {
        return AsyncObservable.Using(async token => await refCountConnection.GetAsync(token),
            connectionRef =>
            {
                var connection = connectionRef.Value;
                return AsyncObservable.Create<T>(async (innerObs, subscriptionToken) =>
                {
                    var subscription = new RedisValuesSubscription(connection, options, key);
                    try
                    {
                        await subscription.SubscribeAsync(innerObs, subscriptionToken);
                    }
                    catch
                    {
                        await subscription.DisposeAsync();
                        throw;
                    }

                    return subscription;
                }).TakeUntil(completionSignal =>
                {
                    connection.ConnectionFailed += ConnectionOnConnectionFailed;
                    return AsyncDisposable.Create(() => connection.ConnectionFailed -= ConnectionOnConnectionFailed);
                    void ConnectionOnConnectionFailed(object? sender, ConnectionFailedEventArgs e) => completionSignal(Result.Failure(e.Exception ?? new Exception("Redis disconnected")));
                }, new TakeUntilOptions { SourceFailsWhenOtherFails = true });
            });
    }
    sealed class RedisValuesSubscription(IConnectionMultiplexer connection, RedisReplayLatestSubjectStoreOptions options, string key)
           : IAsyncDisposable
    {
        static readonly BoundedChannelOptions PendingChannelOptions = new(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = true,
        };

        string VersionKey => $"{key}:version";
        int _disposed;
        bool _consuming;
        long _lastVersion;
        readonly AsyncLocal<bool> _reentrant = new();
        readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly CancellationTokenSource _cts = new();
        ISubscriber? _subscriber;
        ChannelMessageQueue? _queue;
        readonly RedisChannel _channel = RedisChannel.Literal(key);

        public async ValueTask SubscribeAsync(AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            _subscriber = connection.GetSubscriber();
            _queue = await _subscriber.SubscribeAsync(_channel);

            var pending = Channel.CreateBounded<ChannelMessage>(PendingChannelOptions);

            var db = connection.GetDatabase();
            var values = await db.StringGetAsync([key, VersionKey]);
            var current = values[0];
            _lastVersion = values[1].IsNull ? 0L : (long)values[1];

            if (!current.IsNull)
            {
                try
                {
                    var notification = JsonSerializer.Deserialize<Notification<T>>((string)current!, options.SerializerOptions)!;
                    await notification.ForwardTo(observer, cancellationToken);
                }
                catch (Exception error)
                {
                    await observer.OnErrorResumeAsync(error, cancellationToken);
                }
            }

            _queue.OnMessage(message => pending.Writer.TryWrite(message));
            _consuming = true;
            ConsumeAsync(pending.Reader, observer, _cts.Token);
        }


        async void ConsumeAsync(ChannelReader<ChannelMessage> reader, AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            try
            {
                _reentrant.Value = true;
                await foreach (var message in reader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        await OnMessageAsync(message, observer, cancellationToken);
                    }
                    catch (Exception e)
                    {
                        await observer.OnErrorResumeAsync(e, cancellationToken);
                    }
                }
            }
            catch
            {
                // Ignored
            }
            finally
            {
                _tcs.TrySetResult(true);
            }
        }

        async ValueTask OnMessageAsync(ChannelMessage message, AsyncObserver<T> observer, CancellationToken cancellationToken)
        {
            var raw = (string?)message.Message;
            if (raw is null)
                return;

            var span = raw.AsSpan();
            var separator = span.IndexOf('|');
            if (separator < 0)
            {
                await observer.OnErrorResumeAsync(
                    new SignalsStoreRedisException($"Malformed notification for '{key}'."),
                    cancellationToken);
                return;
            }

            var versionPart = span[..separator];

            var notification = JsonSerializer.Deserialize<Notification<T>>(span[(separator + 1)..], options.SerializerOptions)!;
            if (versionPart.Length > 0)
            {
                var version = long.Parse(versionPart);
                if (version <= _lastVersion)
                    return;

                _lastVersion = version;
            }

            await notification.ForwardTo(observer, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                if (_queue is not null and var queue)
                {
                    await queue.UnsubscribeAsync();
                }
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                if (_subscriber is not null and var subscriber)
                {
                    await subscriber.UnsubscribeAsync(_channel);
                }
            }
            catch (ObjectDisposedException)
            {

            }

            _cts.Cancel();

            if (_consuming && !_reentrant.Value)
            {
                await _tcs.Task;
            }

            _cts.Dispose();
        }
    }
}
