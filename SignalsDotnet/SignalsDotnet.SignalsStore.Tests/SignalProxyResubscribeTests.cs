using FluentAssertions;
using R3;
using SignalsDotnet.SignalsStore.Tests.Helpers;

namespace SignalsDotnet.SignalsStore.Tests;

public class SignalProxyResubscribeTests
{
    [Fact]
    public async Task WritesAreNotified_AfterUnsubscribeAndResubscribe()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<long>();
        upstream.ReleaseConnect();
        upstream.ReleaseDisconnect();
        var proxy = new SignalProxy<long>("id", 0L, upstream);

        var first = new List<long>();
        using (proxy.Values.Subscribe(x => first.Add(x)))
        {
            proxy.Value = 1;
            proxy.Value = 2;
        }

        first.Should().Equal(0, 1, 2);

        var second = new List<long>();
        using (proxy.Values.Subscribe(x => second.Add(x)))
        {
            proxy.Value = 3;
            proxy.Value = 4;
        }

        second.Should().Equal(2, 3, 4);
    }

    [Fact]
    public async Task ComputedIsRecomputed_AfterUnsubscribeAndResubscribe()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<long>();
        upstream.ReleaseConnect();
        upstream.ReleaseDisconnect();
        var proxy = new SignalProxy<long>("id", 0L, upstream);
        var computed = Signal.Computed(() => proxy.Value + 22);

        using (computed.Values.Subscribe(_ => { }))
        {
            proxy.Value = 1;
        }

        var seen = new List<long>();
        using (computed.Values.Subscribe(x => seen.Add(x)))
        {
            proxy.Value = 2;
            proxy.Value = 3;
        }

        seen.Should().Equal(23, 24, 25);
    }
}
