using System.ComponentModel;
using FluentAssertions;
using R3;
using SignalsDotnet.SignalsStore.Tests.Helpers;
using State = SignalsDotnet.SignalsStore.ConnectionState;

namespace SignalsDotnet.SignalsStore.Tests;

public class SignalProxyTests
{
    static ISignalProxy<int> CreateProxy(ControllableUpstream<int> upstream, int startValue = 0) =>
        new SignalProxy<int>("id", startValue, upstream);

    [Fact]
    public async Task InitialState_IsDisconnected_WithNoObservers()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        var proxy = CreateProxy(upstream);

        proxy.ConnectionState.UntrackedValue.Should().BeOfType<State.Disconnected>();
        proxy.HasValueObserver.UntrackedValue.Should().BeFalse();
    }

    [Fact(Timeout = 2000)]
    public async Task FirstSubscriber_MovesStateToConnecting_BeforeUpstreamConnects()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        var proxy = CreateProxy(upstream);

        using var _ = proxy.Values.Subscribe(x => { });

        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connecting);
        proxy.HasValueObserver.UntrackedValue.Should().BeTrue();
        upstream.ConnectCount.Should().Be(0);
    }

    [Fact(Timeout = 2000)]
    public async Task StateBecomesConnected_OnlyAfterUpstreamSubscribeCompletes()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        var proxy = CreateProxy(upstream);

        using var _ = proxy.Values.Subscribe(x => { });
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connecting);

        upstream.ReleaseConnect();
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connected);

        upstream.ConnectCount.Should().Be(1);
        proxy.ConnectionState.UntrackedValue.Should().BeOfType<State.Connected>();
    }

    [Fact(Timeout = 2000)]
    public async Task EnsureConnectedAsync_ThrowsWithoutObservers()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        var proxy = CreateProxy(upstream);

        var act = () => proxy.EnsureConnectedAsync(CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact(Timeout = 2000)]
    public async Task EnsureConnectedAsync_CompletesImmediately_WhenAlreadyConnected()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        upstream.ReleaseConnect();
        var proxy = CreateProxy(upstream);

        using var _ = proxy.Values.Subscribe(x => { });
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connected);

        await proxy.EnsureConnectedAsync(CancellationToken.None);
    }

    [Fact(Timeout = 2000)]
    public async Task EnsureConnectedAsync_WaitsForConnectionToComeUp()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        var proxy = CreateProxy(upstream);

        using var _ = proxy.Values.Subscribe(x => { });
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connecting);

        var ensureTask = proxy.EnsureConnectedAsync(CancellationToken.None).AsTask();
        ensureTask.IsCompleted.Should().BeFalse();

        upstream.ReleaseConnect();

        await ensureTask;
        proxy.ConnectionState.UntrackedValue.Should().BeOfType<State.Connected>();
    }


    [Fact(Timeout = 2000)]
    public async Task EnsureConnectedAsync_Throws_WhenConnectFailsWhileWaiting()
    {
        await this.SwitchToMainThread();

        // EnsureConnectedAsync only ever waits for the *first* transition out of Connecting: it
        // resolves the instant the upstream connects, and cannot observe a failure that happens
        // afterwards. So the failure here must happen during the connect attempt itself.
        var upstream = new ControllableUpstream<int> { ConnectThrows = true };
        var proxy = CreateProxy(upstream);

        using var _ = proxy.Values.Subscribe(x => { });
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connecting);

        var ensureTask = proxy.EnsureConnectedAsync(CancellationToken.None).AsTask();
        ensureTask.IsCompleted.Should().BeFalse();

        upstream.ReleaseConnect();

        var act = () => ensureTask;
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact(Timeout = 2000)]
    public async Task Disconnect_IsNotReported_UntilUpstreamDisposeCompletes()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        upstream.ReleaseConnect();
        var proxy = CreateProxy(upstream);

        var subscription = proxy.Values.Subscribe(x => { });
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connected);

        subscription.Dispose();

        // Fire-and-forget dispose: the R3-side Dispose call has already returned, but the
        // async disconnect is gated, so the proxy must still report Connected.
        proxy.ConnectionState.UntrackedValue.Should().BeOfType<State.Connected>();
        upstream.DisconnectCount.Should().Be(0);

        upstream.ReleaseDisconnect();
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Disconnected);

        upstream.DisconnectCount.Should().Be(1);
        proxy.HasValueObserver.UntrackedValue.Should().BeFalse();
    }

    [Fact(Timeout = 2000)]
    public async Task MultipleConcurrentSubscribers_ShareTheSingleUpstreamConnection()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        upstream.ReleaseConnect();
        upstream.ReleaseDisconnect();
        var proxy = CreateProxy(upstream);

        var sub1 = proxy.Values.Subscribe(x => { });
        var sub2 = proxy.Values.Subscribe(x => { });
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connected);

        proxy.HasValueObserver.UntrackedValue.Should().BeTrue();
        upstream.ConnectCount.Should().Be(1);

        sub1.Dispose();
        proxy.HasValueObserver.UntrackedValue.Should().BeTrue();
        upstream.DisconnectCount.Should().Be(0);

        sub2.Dispose();
        await TestHelpers.WaitUntil(() => upstream.DisconnectCount == 1);
        proxy.HasValueObserver.UntrackedValue.Should().BeFalse();
    }

    [Fact(Timeout = 2000)]
    public async Task FastUnsubscribeResubscribe_NeverOpensASecondOverlappingConnection()
    {
        await this.SwitchToMainThread();

        // Share() serializes the ref-counted connect/disconnect: leaving and immediately
        // rejoining reuses the still-live shared connection instead of racing a disconnect
        // against a fresh connect. So this must never observe two connections open at once --
        // it may not even observe a disconnect/reconnect cycle at all if the resubscribe lands
        // before the ref-count actually reaches zero.
        var upstream = new ControllableUpstream<int>();
        upstream.ReleaseConnect();
        upstream.ReleaseDisconnect();
        var proxy = CreateProxy(upstream);

        var sub1 = proxy.Values.Subscribe(x => { });
        await TestHelpers.WaitUntil(() => upstream.ConnectCount == 1);

        sub1.Dispose();
        var sub2 = proxy.Values.Subscribe(x => { });

        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connected);

        // Whatever happened, there is never more than one live connection.
        (upstream.ConnectCount - upstream.DisconnectCount).Should().Be(1);

        sub2.Dispose();
    }

    [Fact(Timeout = 2000)]
    public async Task Values_ReceivesUpstreamEmissions_WhileConnected()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        upstream.ReleaseConnect();
        var proxy = CreateProxy(upstream, startValue: -1);

        var received = new List<int>();
        using var _ = proxy.Values.Subscribe(x => received.Add(x));
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connected);

        await upstream.EmitAsync(1);
        await upstream.EmitAsync(2);

        received.Should().Equal(-1, 1, 2);
    }

    [Fact(Timeout = 2000)]
    public async Task Values_ReplaysStartValue_ToNewSubscriber()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        var proxy = CreateProxy(upstream, startValue: 42);

        var received = new List<int>();
        using var _ = proxy.Values.Subscribe(x => received.Add(x));

        received.Should().Equal(42);
    }

    [Fact(Timeout = 2000)]
    public async Task UpstreamFailure_MovesState_ToDisconnectedWithError()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        upstream.ReleaseConnect();
        var proxy = CreateProxy(upstream);

        using var _ = proxy.Values.Subscribe(x => { });
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connected);

        var error = new InvalidOperationException("upstream failed");
        await upstream.CompleteAsync(error);

        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Disconnected);

        var disconnected = proxy.ConnectionState.UntrackedValue.Should().BeOfType<State.Disconnected>().Subject;
        disconnected.Error.Should().Be(error);
    }

    [Fact(Timeout = 2000)]
    public async Task EnsureConnectedAsync_ReconnectsAfterUpstreamFailure_WithoutRequiringResubscribe()
    {
        await this.SwitchToMainThread();

        // The subscriber stays attached throughout: no unsubscribe/resubscribe, so the only thing
        // that can make the proxy reconnect is EnsureConnectedAsync forcing a fresh attempt.
        var upstream = new ControllableUpstream<int>();
        upstream.ReleaseConnect();
        upstream.ReleaseDisconnect();
        var proxy = CreateProxy(upstream);

        var received = new List<int>();
        using var _ = proxy.Values.Subscribe(x => received.Add(x));
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connected);

        var error = new InvalidOperationException("connection dropped");
        await upstream.CompleteAsync(error);
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Disconnected);

        await proxy.EnsureConnectedAsync(CancellationToken.None);

        proxy.ConnectionState.UntrackedValue.Should().BeOfType<State.Connected>();
        upstream.ConnectCount.Should().Be(2);

        // The reconnected upstream's values must reach the subscriber that stayed attached
        // throughout, not only a subscriber that (re)subscribes after the reconnect.
        await upstream.EmitAsync(99);
        await TestHelpers.WaitUntil(() => received.Contains(99));
    }

    [Fact(Timeout = 2000)]
    public async Task Resubscribing_AfterFullDisconnect_ReconnectsUpstream()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        upstream.ReleaseConnect();
        upstream.ReleaseDisconnect();
        var proxy = CreateProxy(upstream);

        using (var _ = proxy.Values.Subscribe(x => { }))
        {
            await TestHelpers.WaitUntil(() => upstream.ConnectCount == 1);
        }

        await TestHelpers.WaitUntil(() => upstream.DisconnectCount == 1);
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Disconnected);

        using var __ = proxy.Values.Subscribe(x => { });
        await TestHelpers.WaitUntil(() => upstream.ConnectCount == 2);
    }

    [Fact(Timeout = 2000)]
    public async Task PropertyChangedSubscriber_CountsAsExternalSubscriber_ForEnsureConnectedAsync()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        var proxy = CreateProxy(upstream);
        var notifier = (INotifyPropertyChanged)proxy;

        PropertyChangedEventHandler handler = (_, _) => { };
        notifier.PropertyChanged += handler;

        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connecting);

        var ensureTask = proxy.EnsureConnectedAsync(CancellationToken.None).AsTask();
        ensureTask.IsCompleted.Should().BeFalse();

        upstream.ReleaseConnect();

        await ensureTask;
        proxy.ConnectionState.UntrackedValue.Should().BeOfType<State.Connected>();

        notifier.PropertyChanged -= handler;
    }

    [Fact(Timeout = 2000)]
    public async Task ComputedReadingTheProxy_CountsAsExternalSubscriber_ForEnsureConnectedAsync()
    {
        await this.SwitchToMainThread();

        // A Computed collects whatever signal Value's getter reports as the dependency, then
        // re-subscribes to *that* signal directly. If the proxy delegated Value to an inner signal,
        // the Computed would subscribe to the inner one and bypass the proxy's ref-count entirely,
        // leaving HasValueObserver stale and making EnsureConnectedAsync throw.
        var upstream = new ControllableUpstream<int>();
        var proxy = CreateProxy(upstream, startValue: 7);

        var computed = Signal.Computed(() => proxy.Value * 2);
        using var _ = computed.Values.Subscribe(x => { });

        proxy.HasValueObserver.UntrackedValue.Should().BeTrue();
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connecting);

        var ensureTask = proxy.EnsureConnectedAsync(CancellationToken.None).AsTask();
        upstream.ReleaseConnect();

        await ensureTask;
        proxy.ConnectionState.UntrackedValue.Should().BeOfType<State.Connected>();
    }

    [Fact(Timeout = 2000)]
    public async Task ComputedReadingTheProxy_RecomputesOnUpstreamEmissions()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        upstream.ReleaseConnect();
        var proxy = CreateProxy(upstream, startValue: 1);

        var computed = Signal.Computed(() => proxy.Value * 2);
        var received = new List<int>();
        using var _ = computed.Values.Subscribe(x => received.Add(x));

        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connected);

        await upstream.EmitAsync(5);
        await TestHelpers.WaitUntil(() => received.Contains(10));

        received.Should().Equal(2, 10);
    }

    [Fact(Timeout = 2000)]
    public async Task NonGenericValues_CountsAsExternalSubscriber()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        upstream.ReleaseConnect();
        upstream.ReleaseDisconnect();
        var proxy = CreateProxy(upstream);

        var subscription = ((IReadOnlySignal)proxy).Values.Subscribe(_ => { });
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connected);

        proxy.HasValueObserver.UntrackedValue.Should().BeTrue();

        subscription.Dispose();
        await TestHelpers.WaitUntil(() => upstream.DisconnectCount == 1);
        proxy.HasValueObserver.UntrackedValue.Should().BeFalse();
    }

    [Fact(Timeout = 2000)]
    public async Task ThrowingConnectionStateObserver_DoesNotCorruptLifecycle()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        upstream.ReleaseConnect();
        upstream.ReleaseDisconnect();
        var proxy = CreateProxy(upstream);

        using var hostile = proxy.ConnectionState.Values.Subscribe(_ => throw new InvalidOperationException("boom"));
        using var hostileCount = proxy.HasValueObserver.Values.Subscribe(_ => throw new InvalidOperationException("boom"));

        var subscription = proxy.Values.Subscribe(x => { });
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Connected);

        subscription.Dispose();
        await TestHelpers.WaitUntil(() => proxy.ConnectionState.UntrackedValue is State.Disconnected);

        upstream.ConnectCount.Should().Be(1);
        upstream.DisconnectCount.Should().Be(1);
        proxy.HasValueObserver.UntrackedValue.Should().BeFalse();
    }
}
