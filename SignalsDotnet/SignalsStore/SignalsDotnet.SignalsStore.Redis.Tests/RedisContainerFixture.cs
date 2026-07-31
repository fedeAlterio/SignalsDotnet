using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace SignalsDotnet.SignalsStore.Redis.Tests;

public sealed class RedisContainerFixture : IAsyncLifetime
{
    readonly RedisContainer _container = new RedisBuilder()
        .WithImage("redis:7")
        .Build();

    public IConnectionMultiplexer Connection { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;

    static RedisContainerFixture()
    {
        // Ryuk (Testcontainers' auto-cleanup sidecar) is not needed here: DisposeAsync always
        // stops the container itself. Ryuk's own readiness wait has been flaky on this Docker setup
        // (Rancher Desktop/WSL2 host-port forwarding lags behind the container becoming ready,
        // the same symptom worked around below for Redis), turning a reaper that's meant to be a
        // safety net into a hard failure on every run.
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
        Connection = await ConnectWithRetryAsync(ConnectionString);
    }

    // Testcontainers' readiness check (redis-cli ping) runs inside the container's network
    // namespace, so it can pass slightly before the host-side port mapping is actually reachable
    // (observed with Rancher Desktop's WSL2 port forwarding). Retry the first connection instead of
    // failing outright.
    static async Task<IConnectionMultiplexer> ConnectWithRetryAsync(string connectionString)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ConnectionMultiplexer.ConnectAsync(connectionString);
            }
            catch (RedisConnectionException) when (attempt < 15)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (Connection is not null)
            await Connection.DisposeAsync();

        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class RedisContainerCollection : ICollectionFixture<RedisContainerFixture>
{
    public const string Name = "Redis container";
}
