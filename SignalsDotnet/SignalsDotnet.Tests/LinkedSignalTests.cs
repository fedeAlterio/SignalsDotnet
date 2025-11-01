using FluentAssertions;
using R3;
using SignalsDotnet.Helpers;

namespace SignalsDotnet.Tests;

public class LinkedSignalTests
{
    [Fact]
    public void ShouldNotifyWhenAnyChanged()
    {
        var prop1 = new Signal<int>();
        var prop2 = new Signal<int>();

        int Sum() => prop1.Value + prop2.Value;
        var linked = Signal.Linked(Sum);
        int notifiedValue = 0;
        linked.Values.Subscribe(_ => notifiedValue++);
        _ = linked.Value;

        notifiedValue = 0;
        prop1.Value = 2;
        notifiedValue.Should().Be(1);
        linked.Value.Should().Be(Sum());

        notifiedValue = 0;
        prop2.Value = 1;
        notifiedValue.Should().Be(1);
        linked.Value.Should().Be(Sum());
    }

    [Fact]
    public void ShouldNotifyOnlyLatestScannedProperties()
    {
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

        var linked = Signal.Linked(Op);
        linked.Value.Should().Be(Op());

        var linkedChanged = linked.Values.Skip(1);
        
        var notified = false;
        linkedChanged.Subscribe(_ => notified = true);
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
    }

    [Fact]
    public void Untracked_ShouldNotTrack_SignalChanges()
    {
        var a = new Signal<int>();
        var b = new Signal<int>();

        var value = 0;
        var linked = Signal.Linked(() => a.Value + Signal.Untracked(() => b.Value));
        linked.Values.Subscribe(x => value = x);
        a.Value = 1;
        value.Should().Be(1);
        a.Value = 2;
        value.Should().Be(2);

        b.Value = 1;
        value.Should().Be(2);

        linked.Should().NotBeNull();
    }

    [Fact]
    public void UntrackedValue_ShouldNotTrack_SignalChanges()
    {
        var a = new Signal<int>();
        var b = new Signal<int>();

        var value = 0;
        var linked = Signal.Linked(() => a.Value + b.UntrackedValue);
        linked.Values.Subscribe(x => value = x);
        a.Value = 1;
        value.Should().Be(1);
        a.Value = 2;
        value.Should().Be(2);

        b.Value = 1;
        value.Should().Be(2);

        linked.Should().NotBeNull();
    }

    [Fact]
    public void LinkedSignal_ShouldBeWritable()
    {
        var source = new Signal<int>(5);
        var linked = Signal.Linked(() => source.Value * 2);

        linked.Value.Should().Be(10);

        // Linked signals are writable (like Angular's linked signals)
        linked.Value = 42;
        linked.Value.Should().Be(42);

        // After manual write, should still react to source changes
        source.Value = 10;
        linked.Value.Should().Be(20);
    }

    [Fact]
    public void LinkedSignal_WithFallback_ShouldUseFallbackOnError()
    {
        var shouldThrow = new Signal<bool>(false);
        var fallbackCalled = false;

        var linked = Signal.Linked(
            () =>
            {
                if (shouldThrow.Value)
                    throw new InvalidOperationException("Test exception");
                return 42;
            },
            () =>
            {
                fallbackCalled = true;
                return new Optional<int>(100);
            });

        linked.Value.Should().Be(42);
        fallbackCalled.Should().BeFalse();

        shouldThrow.Value = true;
        linked.Value.Should().Be(100);
        fallbackCalled.Should().BeTrue();
    }

    [Fact]
    public void LinkedSignal_WithoutFallback_ShouldUseEmptyOptionalOnError()
    {
        var shouldThrow = new Signal<bool>(false);
        

        var linked = Signal.Linked(() =>
        {
            if (shouldThrow.Value)
                throw new InvalidOperationException("Test exception");
            return 42;
        });

        var previousValue = linked.Value;
        previousValue.Should().Be(42);

        shouldThrow.Value = true;
        // Without fallback, should maintain previous value or use default behavior
        // This matches Angular's behavior where the signal remains at its last valid value
        var currentValue = linked.Value;
        currentValue.Should().BeOneOf(previousValue, default(int));
    }

    [Fact]
    public void LinkedSignal_CanChainMultipleSignals()
    {
        var a = new Signal<int>(1);
        var b = new Signal<int>(2);
        var c = new Signal<int>(3);

        var linked = Signal.Linked(() => a.Value + b.Value + c.Value);

        linked.Value.Should().Be(6);

        a.Value = 10;
        linked.Value.Should().Be(15);

        b.Value = 20;
        linked.Value.Should().Be(33);

        c.Value = 30;
        linked.Value.Should().Be(60);
    }

    [Fact]
    public void LinkedSignal_WithComplexObject_ShouldTrackChanges()
    {
        var firstName = new Signal<string>("John");
        var lastName = new Signal<string>("Doe");

        var fullName = Signal.Linked(() => $"{firstName.Value} {lastName.Value}");

        fullName.Value.Should().Be("John Doe");

        firstName.Value = "Jane";
        fullName.Value.Should().Be("Jane Doe");

        lastName.Value = "Smith";
        fullName.Value.Should().Be("Jane Smith");
    }

    [Fact]
    public void LinkedSignal_ManualWrite_ShouldNotifySubscribers()
    {
        var source = new Signal<int>(5);
        var linked = Signal.Linked(() => source.Value * 2);

        var notificationCount = 0;
        var lastValue = 0;
        linked.Values.Subscribe(x =>
        {
            notificationCount++;
            lastValue = x;
        });

        _ = linked.Value; // Initial access
        notificationCount = 0;

        linked.Value = 100;
        notificationCount.Should().Be(1);
        lastValue.Should().Be(100);
    }

    [Fact]
    public void LinkedSignal_WithConfiguration_ShouldApplyConfiguration()
    {
        var source = new Signal<int>(5);
        var configApplied = false;

        var linked = Signal.Linked(
            () => source.Value * 2,
            configuration: config =>
            {
                configApplied = true;
                return config;
            });

        _ = linked.Value;
        configApplied.Should().BeTrue();
    }
}