using FluentAssertions;
using R3;
using SignalsDotnet.SignalsStore.Tests.Helpers;
using State = SignalsDotnet.SignalsStore.ConnectionState;

namespace SignalsDotnet.SignalsStore.Tests;

public class SignalProxyResubscribeTests
{
    const int TestTimeoutMs = 5000;

    [Fact(Timeout = TestTimeoutMs)]
    public async Task UpstreamValuesAreNotified_AfterUnsubscribeAndResubscribe()
    {
        await this.SwitchToMainThread();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(TestTimeoutMs));

        var upstream = new ControllableUpstream<long>();
        upstream.ReleaseConnect();
        upstream.ReleaseDisconnect();
        ISignalProxy<long> proxy = new SignalProxy<long>("id", 0L, upstream);

        var first = new List<long>();
        using (proxy.Values.Subscribe(x => first.Add(x)))
        {
            await TestHelpers.WaitUntil(() => upstream.ConnectCount == 1);
            await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connected);
            await upstream.EmitAsync(1, cts.Token);
            await upstream.EmitAsync(2, cts.Token);
        }

        first.Should().Equal(0, 1, 2);

        var second = new List<long>();
        using (proxy.Values.Subscribe(x => second.Add(x)))
        {
            await TestHelpers.WaitUntil(() => upstream.ConnectCount == 2);
            await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connected);
            await upstream.EmitAsync(3, cts.Token);
            await upstream.EmitAsync(4, cts.Token);
        }

        second.Should().Equal(2, 3, 4);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ComputedIsRecomputed_AfterUnsubscribeAndResubscribe()
    {
        await this.SwitchToMainThread();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(TestTimeoutMs));

        var upstream = new ControllableUpstream<long>();
        upstream.ReleaseConnect();
        upstream.ReleaseDisconnect();
        ISignalProxy<long> proxy = new SignalProxy<long>("id", 0L, upstream);
        var computed = Signal.Computed(() => proxy.Value + 22);

        using (computed.Values.Subscribe(_ => { }))
        {
            await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connected);
            await upstream.EmitAsync(1, cts.Token);
        }

        var seen = new List<long>();
        using (computed.Values.Subscribe(x => seen.Add(x)))
        {
            await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connected);
            await upstream.EmitAsync(2, cts.Token);
            await upstream.EmitAsync(3, cts.Token);
        }

        seen.Should().Equal(23, 24, 25);
    }
}
