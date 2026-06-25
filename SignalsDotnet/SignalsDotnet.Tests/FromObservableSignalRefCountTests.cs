using FluentAssertions;
using R3;
using SignalsDotnet.Configuration;
using SignalsDotnet.Tests.Helpers;

namespace SignalsDotnet.Tests;

public class FromObservableSignalRefCountTests
{
    [Fact]
    public async Task RefCount_DoesNotSubscribeUpstream_BeforeAnyoneListens()
    {
        await this.SwitchToMainThread();

        var subscribeCount = 0;
        var subject = new Subject<int>();
        var upstream = new TrackingObservable<int>(subject, () => subscribeCount++, () => { });
        var signal = upstream.ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        subscribeCount.Should().Be(0);
    }

    [Fact]
    public async Task RefCount_SubscribesUpstream_WhenFirstValuesSubscriberArrives()
    {
        await this.SwitchToMainThread();

        var subscribeCount = 0;
        var subject = new Subject<int>();
        var upstream = new TrackingObservable<int>(subject, () => subscribeCount++, () => { });
        var signal = upstream.ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        using var _ = signal.Values.Subscribe(_ => { });

        subscribeCount.Should().Be(1);
    }

    [Fact]
    public async Task RefCount_SubscribesUpstream_WhenFirstFutureValuesSubscriberArrives()
    {
        await this.SwitchToMainThread();

        var subscribeCount = 0;
        var subject = new Subject<int>();
        var upstream = new TrackingObservable<int>(subject, () => subscribeCount++, () => { });
        var signal = upstream.ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        using var _ = signal.FutureValues.Subscribe(_ => { });

        subscribeCount.Should().Be(1);
    }

    [Fact]
    public async Task RefCount_UnsubscribesUpstream_WhenLastSubscriberLeaves()
    {
        await this.SwitchToMainThread();

        var unsubscribeCount = 0;
        var subject = new Subject<int>();
        var upstream = new TrackingObservable<int>(subject, () => { }, () => unsubscribeCount++);
        var signal = upstream.ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        var subscription = signal.Values.Subscribe(_ => { });
        unsubscribeCount.Should().Be(0);

        subscription.Dispose();

        unsubscribeCount.Should().Be(1);
    }

    [Fact]
    public async Task RefCount_SingleUpstreamSubscription_WithMultipleConcurrentSubscribers()
    {
        await this.SwitchToMainThread();

        var subscribeCount = 0;
        var subject = new Subject<int>();
        var upstream = new TrackingObservable<int>(subject, () => subscribeCount++, () => { });
        var signal = upstream.ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        using var sub1 = signal.Values.Subscribe(_ => { });
        using var sub2 = signal.Values.Subscribe(_ => { });
        using var sub3 = signal.FutureValues.Subscribe(_ => { });

        subscribeCount.Should().Be(1);
    }

    [Fact]
    public async Task RefCount_KeepsUpstreamSubscribed_UntilAllSubscribersLeave()
    {
        await this.SwitchToMainThread();

        var unsubscribeCount = 0;
        var subject = new Subject<int>();
        var upstream = new TrackingObservable<int>(subject, () => { }, () => unsubscribeCount++);
        var signal = upstream.ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        var sub1 = signal.Values.Subscribe(_ => { });
        var sub2 = signal.FutureValues.Subscribe(_ => { });

        sub1.Dispose();
        unsubscribeCount.Should().Be(0);

        sub2.Dispose();
        unsubscribeCount.Should().Be(1);
    }

    [Fact]
    public async Task RefCount_ResubscribesUpstream_WhenNewSubscriberArrivesAfterAllLeft()
    {
        await this.SwitchToMainThread();

        var subscribeCount = 0;
        var subject = new Subject<int>();
        var upstream = new TrackingObservable<int>(subject, () => subscribeCount++, () => { });
        var signal = upstream.ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        using (var _ = signal.Values.Subscribe(_ => { })) { }
        subscribeCount.Should().Be(1);

        using (var _ = signal.Values.Subscribe(_ => { })) { }
        subscribeCount.Should().Be(2);
    }

    [Fact]
    public async Task RefCount_EmitsValues_WhileSubscribed()
    {
        await this.SwitchToMainThread();

        var subject = new Subject<int>();
        var signal = ((Observable<int>)subject).ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        var received = new List<int>();
        using var _ = signal.FutureValues.Subscribe(x => received.Add(x));

        subject.OnNext(1);
        subject.OnNext(2);
        subject.OnNext(3);

        received.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task RefCount_StopsEmitting_AfterLastSubscriberLeaves()
    {
        await this.SwitchToMainThread();

        var subject = new Subject<int>();
        var signal = ((Observable<int>)subject).ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        var received = new List<int>();
        var subscription = signal.FutureValues.Subscribe(x => received.Add(x));

        subject.OnNext(1);
        subscription.Dispose();
        subject.OnNext(2);

        received.Should().Equal(1);
    }

    [Fact]
    public async Task RefCount_ValuesObservable_EmitsCurrentCachedValue_OnSubscribe()
    {
        await this.SwitchToMainThread();

        var subject = new Subject<int>();
        var signal = ((Observable<int>)subject).ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        var received = new List<int>();

        using var warmup = signal.FutureValues.Subscribe(_ => { });
        subject.OnNext(42);

        using var _ = signal.Values.Subscribe(x => received.Add(x));

        received.Should().Equal(42);
    }

    [Fact]
    public async Task RefCount_ValueRead_DoesNotTriggerUpstreamSubscription()
    {
        await this.SwitchToMainThread();

        var subscribeCount = 0;
        var subject = new Subject<int>();
        var upstream = new TrackingObservable<int>(subject, () => subscribeCount++, () => { });
        var signal = upstream.ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        _ = signal.Value;
        _ = signal.Value;

        subscribeCount.Should().Be(0);
    }

    [Fact]
    public async Task RefCount_UnitFutureValues_AlsoParticipatesInRefCount()
    {
        await this.SwitchToMainThread();

        var subscribeCount = 0;
        var unsubscribeCount = 0;
        var subject = new Subject<int>();
        var upstream = new TrackingObservable<int>(subject, () => subscribeCount++, () => unsubscribeCount++);
        INotifySignalChanged signal = upstream.ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        var subscription = signal.FutureValues.Subscribe(_ => { });
        subscribeCount.Should().Be(1);

        subscription.Dispose();
        unsubscribeCount.Should().Be(1);
    }

    [Fact]
    public async Task RefCount_FromObservable_WorksCorrectly()
    {
        await this.SwitchToMainThread();

        var subscribeCount = 0;
        var subject = new Subject<int>();
        var upstream = new TrackingObservable<int>(subject, () => subscribeCount++, () => { });
        var signal = Signal.FromObservable(upstream, x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        subscribeCount.Should().Be(0);

        using var _ = signal.FutureValues.Subscribe(_ => { });
        subscribeCount.Should().Be(1);
    }

    [Fact]
    public async Task NonRefCount_SubscribesUpstream_OnFirstValueRead()
    {
        await this.SwitchToMainThread();

        var subscribeCount = 0;
        var subject = new Subject<int>();
        var upstream = new TrackingObservable<int>(subject, () => subscribeCount++, () => { });
        var signal = upstream.ToSignal();

        subscribeCount.Should().Be(0);

        _ = signal.Value;
        subscribeCount.Should().Be(1);
    }

    [Fact]
    public async Task NonRefCount_SubscribesOnlyOnce_RegardlessOfValueReadCount()
    {
        await this.SwitchToMainThread();

        var subscribeCount = 0;
        var subject = new Subject<int>();
        var upstream = new TrackingObservable<int>(subject, () => subscribeCount++, () => { });
        var signal = upstream.ToSignal();

        _ = signal.Value;
        _ = signal.Value;
        _ = signal.Value;

        subscribeCount.Should().Be(1);
    }

    [Fact]
    public async Task RefCount_MixedValuesAndFutureValues_ShareOneUpstreamSubscription()
    {
        await this.SwitchToMainThread();

        var subscribeCount = 0;
        var unsubscribeCount = 0;
        var subject = new Subject<int>();
        var upstream = new TrackingObservable<int>(subject, () => subscribeCount++, () => unsubscribeCount++);
        var signal = upstream.ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        var sub1 = signal.Values.Subscribe(_ => { });
        var sub2 = signal.FutureValues.Subscribe(_ => { });
        subscribeCount.Should().Be(1);

        sub1.Dispose();
        unsubscribeCount.Should().Be(0);

        sub2.Dispose();
        unsubscribeCount.Should().Be(1);

        var sub3 = signal.FutureValues.Subscribe(_ => { });
        subscribeCount.Should().Be(2);

        sub3.Dispose();
        unsubscribeCount.Should().Be(2);
    }

    [Fact]
    public async Task RefCount_FutureValues_DeliversSynchronousReplayOnActivation()
    {
        await this.SwitchToMainThread();

        var signal = Observable.Return(99).ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        var received = new List<int>();
        using var _ = signal.FutureValues.Subscribe(received.Add);

        received.Should().Equal(99);
    }

    [Fact]
    public async Task RefCount_Values_EmitsReplayedValueOnce_OnFirstActivation()
    {
        await this.SwitchToMainThread();

        var signal = Observable.Return(99).ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        var received = new List<int>();
        using var _ = signal.Values.Subscribe(received.Add);

        received.Should().Equal(99);
    }

    [Fact]
    public async Task RefCount_AsyncComputed_DoesNotComputeUntilListened()
    {
        await this.SwitchToMainThread();

        var computeCount = 0;
        async ValueTask<int> Compute(CancellationToken token = default)
        {
            await Task.Yield();
            Interlocked.Increment(ref computeCount);
            return 42;
        }

        var signal = Signal.AsyncComputed(Compute, 0, configuration: c => c with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        _ = signal.Value;
        await Task.Yield();
        computeCount.Should().Be(0);

        using var sub = signal.FutureValues.Subscribe(_ => { });
        await TestHelpers.WaitUntil(() => computeCount >= 1);
        computeCount.Should().BeGreaterThanOrEqualTo(1);
    }


    [Fact]
    public async Task RefCount_FutureValues_SynchronousSourceWithTake1_TearsDownUpstreamCleanly()
    {
        await this.SwitchToMainThread();

        var subscribeCount = 0;
        var unsubscribeCount = 0;
        var upstream = new TrackingObservable<int>(new SyncEmitObservable<int>(99), () => subscribeCount++, () => unsubscribeCount++);
        var signal = upstream.ToSignal(x => x with { SubscriptionStrategy = SubscriptionStrategy.RefCount });

        using var _ = signal.FutureValues.Take(1).Subscribe(_ => { });

        subscribeCount.Should().Be(1);
        unsubscribeCount.Should().Be(1);
    }


    sealed class SyncEmitObservable<T>(T value) : Observable<T>
    {
        protected override IDisposable SubscribeCore(Observer<T> observer)
        {
            observer.OnNext(value);
            return Disposable.Empty;
        }
    }

    sealed class TrackingObservable<T>(Observable<T> inner, Action onSubscribe, Action onUnsubscribe) : Observable<T>
    {
        protected override IDisposable SubscribeCore(Observer<T> observer)
        {
            onSubscribe();
            var subscription = inner.Subscribe(observer.OnNext, observer.OnErrorResume, observer.OnCompleted);
            return new TrackingDisposable(subscription, onUnsubscribe);
        }

        sealed class TrackingDisposable(IDisposable inner, Action onUnsubscribe) : IDisposable
        {
            int _disposed;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    inner.Dispose();
                    onUnsubscribe();
                }
            }
        }
    }
}
