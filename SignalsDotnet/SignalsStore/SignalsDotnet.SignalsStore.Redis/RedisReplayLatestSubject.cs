using System.Text.Json;
using R3Async;
using R3Async.Subjects;
using StackExchange.Redis;

namespace SignalsDotnet.SignalsStore.Redis;

sealed class RedisReplayLatestSubject<T>(
    RefCountLazy<IConnectionMultiplexer> connection,
    string key,
    RedisReplayLatestSubjectStoreOptions options)
    : ISubject<T>
{
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

    public AsyncObservable<T> Values { get; } = RedisReplayLatestObservable<T>.Create(connection, key, options).Share(ShareConfig.ResetOnCompletionAndRefCountZero);

    readonly RedisChannel _channel = RedisChannel.Literal(key);
    readonly RedisKey[] _publishScriptKeys = [key, $"{key}:version", RedisChannel.Literal(key).ToString()];

    public async ValueTask OnNextAsync(T value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(Notification<T>.ForValue(value), options.SerializerOptions);
        await PublishAsync(json, cancellationToken);
    }

    public async ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(Notification<T>.ForError(error), options.SerializerOptions);
        await using var connectionRef = await connection.GetAsync(cancellationToken);
        await connectionRef.Value.GetSubscriber().PublishAsync(_channel, "|" + json);
    }

    public async ValueTask OnCompletedAsync(Result result)
    {
        var json = JsonSerializer.Serialize(Notification<T>.ForCompletion(result), options.SerializerOptions);
        await PublishAsync(json, CancellationToken.None);
    }

    async ValueTask PublishAsync(string json, CancellationToken cancellationToken)
    {
        await using var connectionRef = await connection.GetAsync(cancellationToken);
        var db = connectionRef.Value.GetDatabase();
        await db.ScriptEvaluateAsync(PublishScript, _publishScriptKeys, [json]);
    }
}
