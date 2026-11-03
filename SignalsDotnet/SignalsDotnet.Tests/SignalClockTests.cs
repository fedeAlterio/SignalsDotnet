using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using R3;
using SignalsDotnet.Tests.Helpers;

namespace SignalsDotnet.Tests;

public class SignalClockTests
{
    const int TestTimeoutMs = 10_000;
    static readonly DateTimeOffset Start = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact(Timeout = TestTimeoutMs)]
    public async Task BeforeDeadlineShouldFlipExactlyAtTheDeadline()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var now = time.ToSignalClock();
        var deadline = new Signal<DateTimeOffset>(Start.AddMinutes(5));
        var computed = Signal.Computed(() => now < deadline.Value);
        computed.Values.Subscribe(_ => { });

        computed.Value.Should().BeTrue();

        time.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));
        computed.Value.Should().BeTrue();

        time.Advance(TimeSpan.FromTicks(1));
        computed.Value.Should().BeFalse();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task StrictComparisonShouldFlipOneTickAfterTheInstant()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var now = time.ToSignalClock();
        var computed = Signal.Computed(() => now > Start.AddMinutes(1));
        computed.Values.Subscribe(_ => { });

        time.Advance(TimeSpan.FromMinutes(1));
        computed.Value.Should().BeFalse();

        time.Advance(TimeSpan.FromTicks(1));
        computed.Value.Should().BeTrue();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ElapsedShouldFlipWhenDurationPasses()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var now = time.ToSignalClock();
        var since = new Signal<DateTimeOffset>(Start);
        var computed = Signal.Computed(() => now - since.Value >= TimeSpan.FromMinutes(5));
        computed.Values.Subscribe(_ => { });

        computed.Value.Should().BeFalse();

        time.Advance(TimeSpan.FromMinutes(3));
        since.Value = time.GetUtcNow();

        time.Advance(TimeSpan.FromMinutes(4));
        computed.Value.Should().BeFalse();

        time.Advance(TimeSpan.FromMinutes(1));
        computed.Value.Should().BeTrue();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task PastInstantShouldNotScheduleRecomputation()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var now = time.ToSignalClock();
        var computations = 0;
        var computed = Signal.Computed(() =>
        {
            computations++;
            return now >= Start.AddMinutes(-1);
        });
        computed.Values.Subscribe(_ => { });

        time.Advance(TimeSpan.FromDays(1));

        computed.Value.Should().BeTrue();
        computations.Should().Be(1);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task DeadlineNoLongerReadShouldBeDropped()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var now = time.ToSignalClock();
        var useClock = new Signal<bool>(true);
        var computations = 0;
        var computed = Signal.Computed(() =>
        {
            computations++;
            return useClock.Value && now < Start.AddMinutes(5);
        });
        computed.Values.Subscribe(_ => { });

        useClock.Value = false;
        computations = 0;

        time.Advance(TimeSpan.FromMinutes(10));

        computations.Should().Be(0);
        computed.Value.Should().BeFalse();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ShouldRecomputeOnceForEarliestDeadline()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var now = time.ToSignalClock();
        var values = new List<int>();
        var computed = Signal.Computed(() => (now >= Start.AddMinutes(1) ? 1 : 0) + (now >= Start.AddMinutes(2) ? 1 : 0));
        computed.Values.Subscribe(values.Add);

        time.Advance(TimeSpan.FromMinutes(1));
        time.Advance(TimeSpan.FromMinutes(1));

        values.Should().Equal(0, 1, 2);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task OutsideComputedShouldBePlainComparison()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var now = time.ToSignalClock();

        (now < Start.AddMinutes(1)).Should().BeTrue();
        (now >= Start).Should().BeTrue();
        (now - Start.AddMinutes(-2) > TimeSpan.FromMinutes(1)).Should().BeTrue();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ElapsedShouldNotOverflow()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var now = time.ToSignalClock();

        (now - DateTimeOffset.MaxValue >= TimeSpan.FromDays(1)).Should().BeFalse();
        (now - DateTimeOffset.MinValue >= TimeSpan.MaxValue).Should().BeFalse();
        (now - Start >= TimeSpan.MinValue).Should().BeTrue();
    }
}
