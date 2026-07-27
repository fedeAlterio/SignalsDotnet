using FluentAssertions;
using R3;
using SignalsDotnet.SignalsStore.Tests.Helpers;

namespace SignalsDotnet.SignalsStore.Tests;

public class SignalProxyWriteTests
{
    [Fact]
    public async Task SettingValue_IsVisibleImmediately_EvenWhileDisconnected()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        var proxy = new SignalProxy<int>("id", startValue: 0, upstream);

        var received = new List<int>();
        using var _ = proxy.Values.Subscribe(x => received.Add(x));

        proxy.Value = 7;

        received.Should().Equal(0, 7);
        proxy.UntrackedValue.Should().Be(7);
    }

    [Fact]
    public async Task SettingValue_ForwardsToTheUpstreamSink()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        var forwarded = new List<int>();
        var proxy = new SignalProxy<int>("id", startValue: 0, upstream, forwarded.Add);

        using var _ = proxy.Values.Subscribe(x => { });

        proxy.Value = 1;
        proxy.Value = 2;

        forwarded.Should().Equal(1, 2);
    }

    [Fact]
    public async Task SettingValue_DoesNotRequireObservers()
    {
        await this.SwitchToMainThread();

        var upstream = new ControllableUpstream<int>();
        var forwarded = new List<int>();
        var proxy = new SignalProxy<int>("id", startValue: 0, upstream, forwarded.Add);

        proxy.Value = 5;

        forwarded.Should().Equal(5);
    }

    [Fact(Timeout = 2000)]
    public async Task StoreWritePipeline_PublishesValues_ToTheSubject()
    {
        await this.SwitchToMainThread();

        var subject = new ControllableSubject<int>();
        var store = new StubSubjectStore(subject).ToSignalsStore();
        var proxy = store.CreateSignalProxy("id", 0);

        proxy.Value = 1;

        await TestHelpers.WaitUntil(() => subject.Received.Count == 1);
        subject.Received.Should().Equal(1);
    }

    [Fact(Timeout = 5000)]
    public async Task StoreWritePipeline_DropsPreviousValue_WhenTheSubjectIsSlow()
    {
        await this.SwitchToMainThread();

        var subject = new ControllableSubject<int>();
        var store = new StubSubjectStore(subject).ToSignalsStore();
        var proxy = store.CreateSignalProxy("id", 0);

        proxy.Value = 1;
        await TestHelpers.WaitUntil(() => subject.Received.Count == 1);

        subject.BlockPublish();
        proxy.Value = 2;
        await TestHelpers.WaitUntil(() => subject.InFlight);

        proxy.Value = 3;
        proxy.Value = 4;

        subject.ReleasePublish();

        await TestHelpers.WaitUntil(() => subject.Received.Count == 3);
        await Task.Delay(100);

        subject.Received.Should().Equal(1, 2, 4);
    }

    [Fact(Timeout = 2000)]
    public async Task StoreWritePipeline_SetterNeverBlocks_OnASlowSubject()
    {
        await this.SwitchToMainThread();

        var subject = new ControllableSubject<int>();
        var store = new StubSubjectStore(subject).ToSignalsStore();
        var proxy = store.CreateSignalProxy("id", 0);

        proxy.Value = 1;
        await TestHelpers.WaitUntil(() => subject.Received.Count == 1);

        subject.BlockPublish();
        proxy.Value = 2;
        await TestHelpers.WaitUntil(() => subject.InFlight);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
            proxy.Value = i;
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
        subject.InFlight.Should().BeTrue();

        subject.ReleasePublish();
    }

    sealed class StubSubjectStore(R3Async.Subjects.ISubject<int> subject) : ISubjectStore
    {
        public R3Async.Subjects.ISubject<T> CreateSubject<T>(string id) =>
            (R3Async.Subjects.ISubject<T>)(object)subject;
    }
}
