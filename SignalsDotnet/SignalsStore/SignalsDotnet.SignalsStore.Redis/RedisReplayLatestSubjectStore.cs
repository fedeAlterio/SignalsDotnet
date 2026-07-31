using System.Text.Json;
using R3Async;
using R3Async.Subjects;
using StackExchange.Redis;

namespace SignalsDotnet.SignalsStore.Redis;

public sealed record RedisReplayLatestSubjectStoreOptions
{
    public string Namespace { get; init; } = "defaultns";

    public JsonSerializerOptions? SerializerOptions { get; init; }
}

public sealed class SignalsStoreRedisException(string message) : Exception(message);

/// <summary>
/// An <see cref="ISubjectStore"/> backed by Redis, with the <see cref="IConnectionMultiplexer"/>
/// built lazily from <paramref name="connectionFactory"/>. The connection is created on the first
/// subscription/write that needs it, shared across every subject created from this store, and
/// disposed once the last one releases it.
/// </summary>
public sealed class RedisReplayLatestSubjectStore(
    Func<CancellationToken, ValueTask<IConnectionMultiplexer>> connectionFactory,
    RedisReplayLatestSubjectStoreOptions? options = null)
    : ISubjectStore
{
    readonly RedisReplayLatestSubjectStoreOptions _options = options ?? new RedisReplayLatestSubjectStoreOptions();

    private readonly RefCountLazy<IConnectionMultiplexer> _connection = new(async cancellationToken =>
    {
        var connection = await connectionFactory(cancellationToken);
        return new AsyncDisposableValue<IConnectionMultiplexer>
        {
            Value = connection,
            Disposable = AsyncDisposable.Create(async () =>
            {
                await using (connection)
                {
                    await connection.CloseAsync();
                }
            })
        };
    });

    public ISubject<T> CreateSubject<T>(string id)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("The id must be a non empty string.", nameof(id));

        return new RedisReplayLatestSubject<T>(_connection, $"{_options.Namespace}:{id}", _options);
    }
}
