using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using R3;
using SignalsDotnet.Tests.Helpers;

namespace SignalsDotnet.Tests;

public class TimeOperatorsTests
{
    const int TestTimeoutMs = 10_000;
    static readonly DateTimeOffset Start = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact(Timeout = TestTimeoutMs)]
    public async Task StampedShouldUseCreationTimeAsFirstChange()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var number = new Signal<int>(1);
        var stamped = number.Stamped(time.ToSignalClock());

        time.Advance(TimeSpan.FromMinutes(3));
        stamped.Values.Subscribe(_ => { });

        stamped.Value.Should().Be(new ChangeStamp<int>(1, Start));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ChangedAtShouldChangeOnlyWhenResultChanges()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var temperature = new Signal<double>(85);
        var changedAt = Signal.ChangedAt(() => temperature.Value > 80, time.ToSignalClock());
        var values = new List<DateTimeOffset>();
        changedAt.Values.Subscribe(values.Add);

        time.Advance(TimeSpan.FromMinutes(1));
        temperature.Value = 90;

        time.Advance(TimeSpan.FromMinutes(1));
        temperature.Value = 70;

        values.Should().Equal(Start, Start.AddMinutes(2));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task HeldForShouldIgnoreShortSpikes()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var temperature = new Signal<double>(85);
        var alarm = Signal.HeldFor(() => temperature.Value > 80, TimeSpan.FromMinutes(5), time.ToSignalClock());
        var values = new List<bool>();
        alarm.Values.Subscribe(values.Add);

        time.Advance(TimeSpan.FromMinutes(3));
        temperature.Value = 70;

        time.Advance(TimeSpan.FromMinutes(1));
        temperature.Value = 90;

        time.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));
        alarm.Value.Should().BeFalse();

        time.Advance(TimeSpan.FromTicks(1));
        alarm.Value.Should().BeTrue();

        time.Advance(TimeSpan.FromMinutes(3));
        temperature.Value = 75;
        alarm.Value.Should().BeFalse();

        values.Should().Equal(false, true, false);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task HeldForShouldWorkOnSignals()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var running = new Signal<bool>(true);
        var heldFor = running.HeldFor(TimeSpan.FromMinutes(1), time.ToSignalClock());
        heldFor.Values.Subscribe(_ => { });

        time.Advance(TimeSpan.FromMinutes(1));

        heldFor.Value.Should().BeTrue();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task HeldForShouldStopWhenFactoryDisconnects()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var disconnected = new Signal<bool>();
        var factory = ComputedSignalFactory.Default.DisconnectEverythingWhen(() => disconnected.Value);
        var computations = 0;
        var alarm = factory.HeldFor(() =>
        {
            computations++;
            return true;
        }, TimeSpan.FromMinutes(5), time.ToSignalClock());
        alarm.Values.Subscribe(_ => { });

        disconnected.Value = true;
        computations = 0;

        time.Advance(TimeSpan.FromMinutes(10));

        alarm.Value.Should().BeFalse();
        computations.Should().Be(0);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task HeldForShouldReportConditionExceptionsToFactory()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var shouldThrow = new Signal<bool>();
        var exceptions = new List<Exception>();
        var factory = ComputedSignalFactory.Default.OnException(exceptions.Add);
        var alarm = factory.HeldFor(() => shouldThrow.Value ? throw new InvalidOperationException() : true,
                                    TimeSpan.FromMinutes(5),
                                    time.ToSignalClock());
        alarm.Values.Subscribe(_ => { });

        shouldThrow.Value = true;

        exceptions.Should().ContainSingle().Which.Should().BeOfType<InvalidOperationException>();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task TrueWithinShouldStayTrueForDurationAfterConditionDrops()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var motor = new Signal<bool>();
        var fan = motor.TrueWithin(TimeSpan.FromMinutes(2), time.ToSignalClock());
        var values = new List<bool>();
        fan.Values.Subscribe(values.Add);

        time.Advance(TimeSpan.FromMinutes(5));
        fan.Value.Should().BeFalse();

        motor.Value = true;
        fan.Value.Should().BeTrue();

        time.Advance(TimeSpan.FromMinutes(1));
        motor.Value = false;

        time.Advance(TimeSpan.FromMinutes(2) - TimeSpan.FromTicks(1));
        fan.Value.Should().BeTrue();

        time.Advance(TimeSpan.FromTicks(1));
        fan.Value.Should().BeFalse();

        values.Should().Equal(false, true, false);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task StableForShouldRestartOnEveryChange()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var recipe = new Signal<string>("A");
        var settled = recipe.StableFor(TimeSpan.FromMinutes(10), time.ToSignalClock());
        settled.Values.Subscribe(_ => { });

        time.Advance(TimeSpan.FromMinutes(6));
        recipe.Value = "B";

        time.Advance(TimeSpan.FromMinutes(6));
        recipe.Value = "C";

        time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromTicks(1));
        settled.Value.Should().BeFalse();

        time.Advance(TimeSpan.FromTicks(1));
        settled.Value.Should().BeTrue();

        recipe.Value = "D";
        settled.Value.Should().BeFalse();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task TrueSinceShouldBeNullWhileFalse()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var inAlarm = new Signal<bool>();
        var since = inAlarm.TrueSince(time.ToSignalClock());
        var values = new List<DateTimeOffset?>();
        since.Values.Subscribe(values.Add);

        time.Advance(TimeSpan.FromMinutes(4));
        inAlarm.Value = true;

        time.Advance(TimeSpan.FromMinutes(3));
        inAlarm.Value = false;

        values.Should().Equal(null, Start.AddMinutes(4), null);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task RisingShouldRememberLastEdge()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var running = new Signal<bool>();
        var rising = running.Rising(time.ToSignalClock());
        var values = new List<DateTimeOffset?>();
        rising.Values.Subscribe(values.Add);

        time.Advance(TimeSpan.FromMinutes(10));
        running.Value = true;

        time.Advance(TimeSpan.FromMinutes(50));
        running.Value = false;
        rising.Value.Should().Be(Start.AddMinutes(10));

        time.Advance(TimeSpan.FromMinutes(30));
        running.Value = true;

        values.Should().Equal(null, Start.AddMinutes(10), Start.AddMinutes(90));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task RisingShouldNotCountInitialTrueAsEdge()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var running = new Signal<bool>(true);
        var rising = running.Rising(time.ToSignalClock());
        rising.Values.Subscribe(_ => { });

        rising.Value.Should().BeNull();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task FallingShouldRememberLastTrueToFalseEdge()
    {
        await this.SwitchToMainThread();
        var time = new FakeTimeProvider(Start);
        var working = new Signal<bool>(true);
        var falling = working.Falling(time.ToSignalClock());
        var values = new List<DateTimeOffset?>();
        falling.Values.Subscribe(values.Add);

        time.Advance(TimeSpan.FromMinutes(5));
        working.Value = false;

        time.Advance(TimeSpan.FromMinutes(5));
        working.Value = true;

        values.Should().Equal(null, Start.AddMinutes(5));
    }
}
