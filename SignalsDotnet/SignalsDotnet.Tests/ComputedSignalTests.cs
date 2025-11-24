using FluentAssertions;
using R3;
using SignalsDotnet.Tests.Helpers;

namespace SignalsDotnet.Tests;

public class ComputedSignalTests
{
    [Fact]
    public async Task ShouldNotifyWhenAnyChanged()
    {
        await this.SwitchToMainThread();
        var prop1 = new Signal<int>();
        var prop2 = new Signal<int>();

        int Sum() => prop1.Value + prop2.Value;
        var computed = Signal.Computed(Sum);
        int notifiedValue = 0;
        computed.Values.Subscribe(_ => notifiedValue++);
        _ = computed.Value;

        notifiedValue = 0;
        prop1.Value = 2;
        notifiedValue.Should().Be(1);
        computed.Value.Should().Be(Sum());

        notifiedValue = 0;
        prop2.Value = 1;
        notifiedValue.Should().Be(1);
        computed.Value.Should().Be(Sum());
    }

    [Fact]
    public async Task ShouldNotifyOnlyLatestScannedProperties()
    {
        await this.SwitchToMainThread();
        var number1 = new Signal<int>();
        var number2 = new Signal<int>();
        var defaultValue = new Signal<int>();
        var shouldReturnDefault = new Signal<bool>();

        int Op()
        {
            if (!shouldReturnDefault.Value)
                return number1.Value - number2.Value;

            return defaultValue.Value;
        }

        var computed = Signal.Computed(Op);
        computed.Value.Should().Be(Op());

        var computedChanged = computed.Values.Skip(1);
        
        var notified = false;
        computedChanged.Subscribe(_ => notified = true);
        defaultValue.Value = 2;
        notified.Should().BeFalse();

        notified = false;
        shouldReturnDefault.Value = true;
        notified.Should().BeTrue();

        notified = false;
        defaultValue.Value = 3;
        notified.Should().BeTrue();

        notified = false;
        shouldReturnDefault.Value = false;
        notified.Should().BeTrue();

        notified = false;
        defaultValue.Value = 11;
        notified.Should().BeFalse();

        notified = false;
    }

    [Fact]
    public async Task Untracked_ShouldNotTrack_SignalChanges()
    {
        await this.SwitchToMainThread();
        var a = new Signal<int>();
        var b = new Signal<int>();

        var value = 0;
        var computed = Signal.Computed(() => a.Value + Signal.Untracked(() => b.Value));
        computed.Values.Subscribe(x => value = x);
        a.Value = 1;
        value.Should().Be(1);
        a.Value = 2;
        value.Should().Be(2);

        b.Value = 1;
        value.Should().Be(2);

        computed.Should().NotBeNull();
    }

    [Fact]
    public async Task UntrackedValue_ShouldNotTrack_SignalChanges()
    {
        await this.SwitchToMainThread();
        var a = new Signal<int>();
        var b = new Signal<int>();

        var value = 0;
        var computed = Signal.Computed(() => a.Value + b.UntrackedValue);
        computed.Values.Subscribe(x => value = x);
        a.Value = 1;
        value.Should().Be(1);
        a.Value = 2;
        value.Should().Be(2);

        b.Value = 1;
        value.Should().Be(2);

        computed.Should().NotBeNull();
    }
}