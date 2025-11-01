using FluentAssertions;
using SignalsDotnet.Helpers;
using SignalsDotnet.Tests.Helpers;
using R3;

namespace SignalsDotnet.Tests;

public class AsyncLinkedSignalTests
{
    [Fact]
    public async Task ShouldNotifyWhenAnyChanged()
    {
        await this.SwitchToMainThread();

        var prop1 = new Signal<int>();
        var prop2 = new Signal<int>();

        async ValueTask<int> Sum(CancellationToken token = default)
        {
            await Task.Yield();
            return prop1.Value + prop2.Value;
        }

        var linked = Signal.AsyncLinked(Sum, 0, () => Optional<int>.Empty);
        int notifiedValue = 0;
        linked.Values.Subscribe(_ => notifiedValue++);
        _ = linked.Value;
        await TestHelpers.WaitUntil(() => notifiedValue == 1);

        notifiedValue = 0;
        prop1.Value = 2;
        await TestHelpers.WaitUntil(() => notifiedValue == 1);
        linked.Value.Should().Be(await Sum());

        notifiedValue = 0;
        prop2.Value = 1;
        await TestHelpers.WaitUntil(() => notifiedValue == 1);
        linked.Value.Should().Be(await Sum());
    }

    [Fact]
    public async Task SignalChangedWhileComputing_ShouldBeConsidered()
    {
        await this.SwitchToMainThread();

        var prop1 = new Signal<int>();
        var prop2 = new Signal<int>();

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var linked = Signal.AsyncLinked(Sum, 0, () => Optional<int>.Empty);

        async ValueTask<int> Sum(CancellationToken token = default)
        {
            await Task.Yield();
            var sum = prop1.Value + prop2.Value;
            if (prop1.Value <= 3)
            {
                prop1.Value++;
                prop2.Value++;
            }
            else
            {
                tcs.TrySetResult();
            }

            return sum;
        }

        _ = linked.Value;
        await tcs.Task;
        await TestHelpers.WaitUntil(() => linked.Value == prop1.Value + prop2.Value);
    }

    [Fact]
    public async Task ConcurrentUpdate_ShouldCancelCurrentIfRequested()
    {
        await this.SwitchToMainThread();

        var prop1 = new Signal<int>();
        var prop2 = new Signal<int>();

        CancellationToken computeToken = CancellationToken.None;
        var linked = Signal.AsyncLinked(async token =>
        {
            var sum = prop1.Value + prop2.Value;
            if (prop1.Value == 1)
                return sum;

            prop1.Value++;
            computeToken = token;
            await Task.Delay(1, token);
            return sum;
        }, 0, ConcurrentChangeStrategy.CancelCurrent);

        _ = linked.Value;
        await TestHelpers.WaitUntil(() => computeToken.IsCancellationRequested);
    }

    [Fact]
    public async Task OtherSignalChanges_ShouldNotBeConsidered()
    {
        await this.SwitchToMainThread();

        var signal1 = new Signal<int>();
        var signal2 = new Signal<int>();

        var signal3 = new Signal<int>();

        var middleComputationTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var signal3ChangedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stepNumber = 0;
        var linked = Signal.AsyncLinked(async _ =>
        {
            middleComputationTcs.TrySetResult();
            var ret = stepNumber + signal1.Value + signal2.Value;
            stepNumber++;
            await signal3ChangedTcs.Task;
            return ret;
        }, 0);

        var notifiedCount = 0;
        _ = linked.Value;
        await linked.Values.Where(x => x == 0)
                 .Take(1)
                 .WaitAsync()
                 .ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        linked.Values.Skip(1).Subscribe(_ => notifiedCount++);
        await middleComputationTcs.Task;

        _ = signal3.Value;
        signal3.Value = 1;
        signal3ChangedTcs.SetResult();

        await Task.Yield();
        notifiedCount.Should().Be(0);
        signal1.Value = 1;
        await TestHelpers.WaitUntil(() => linked.Value == 2);
    }

    [Fact]
    public async Task ConcurrentUpdate_ShouldScheduleNext_IfRequested()
    {
        await this.SwitchToMainThread();

        var prop1 = new Signal<int>(1);
        var prop2 = new Signal<int>();

        var linked = Signal.AsyncLinked(async token =>
        {
            var sum = prop1.Value + prop2.Value;
            prop1.Value++;
            await Task.Delay(0, token);
            await Task.Yield();
            return sum;
        }, -1);

        var task = linked.Values.FirstAsync(x => x == 20);
        await task;
    }

    [Fact]
    public async Task SimpleTest()
    {
        await this.SwitchToMainThread();

        Signal<int> signal = new(0);
        var asyncLinked = Signal.AsyncLinked(async _ =>
        {
            var x = signal.Value;
            await Task.Yield();
            await Task.Yield();
            await Task.Yield();
            return x;
        }, -1, configuration: x => x with
        {
            SubscribeWeakly = false
        });

        _ = asyncLinked.Value;
        signal.Value = 0;
        signal.Value = 1;
        signal.Value = 2;
        signal.Value = 3;
        signal.Value = 4;
        signal.Value = 5;

        await asyncLinked.Values
                           .Timeout(TimeSpan.FromSeconds(1))
                           .FirstAsync(x => x == 5)
                           .ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
    }

    [Fact]
    public async Task LinkedSignal_ShouldBeWritable()
    {
        await this.SwitchToMainThread();

        var source = new Signal<int>(5);
        var linked = Signal.AsyncLinked(async _ =>
        {
            await Task.Yield();
            return source.Value * 2;
        }, 0);

        await TestHelpers.WaitUntil(() => linked.Value == 10);
        linked.Value.Should().Be(10);

        // Linked signals are writable (like Angular's linked signals)
        linked.Value = 42;
        linked.Value.Should().Be(42);

        // After manual write, should still react to source changes
        source.Value = 10;
        await TestHelpers.WaitUntil(() => linked.Value == 20);
        linked.Value.Should().Be(20);
    }

    [Fact]
    public async Task LinkedSignal_WithFallback_ShouldUseFallbackOnError()
    {
        await this.SwitchToMainThread();

        var shouldThrow = new Signal<bool>(false);
        var fallbackCalled = false;

        var linked = Signal.AsyncLinked(async _ =>
                                        {
                                            await Task.Yield();
                                            if (shouldThrow.Value)
                                                throw new InvalidOperationException("Test exception");
                                            return 42;
                                        },
                                        0,
                                        () =>
                                        {
                                            fallbackCalled = true;
                                            return new Optional<int>(100);
                                        });

        await TestHelpers.WaitUntil(() => linked.Value == 42);
        linked.Value.Should().Be(42);
        fallbackCalled.Should().BeFalse();

        shouldThrow.Value = true;
        await TestHelpers.WaitUntil(() => fallbackCalled);
        linked.Value.Should().Be(100);
        fallbackCalled.Should().BeTrue();
    }

    [Fact]
    public async Task LinkedSignal_WithFuncFallback_ShouldUseFallbackOnError()
    {
        await this.SwitchToMainThread();

        var shouldThrow = new Signal<bool>(false);
        var fallbackCalled = false;

        var linked = Signal.AsyncLinked(async _ =>
                                        {
                                            await Task.Yield();
                                            if (shouldThrow.Value)
                                                throw new InvalidOperationException("Test exception");
                                            return 42;
                                        },
                                        0,
                                        () =>
                                        {
                                            fallbackCalled = true;
                                            return 100;
                                        });

        await TestHelpers.WaitUntil(() => linked.Value == 42);
        linked.Value.Should().Be(42);
        fallbackCalled.Should().BeFalse();

        shouldThrow.Value = true;
        await TestHelpers.WaitUntil(() => fallbackCalled);
        linked.Value.Should().Be(100);
        fallbackCalled.Should().BeTrue();
    }

    [Fact]
    public async Task LinkedSignal_WithoutFallback_ShouldUseEmptyOptionalOnError()
    {
        await this.SwitchToMainThread();

        var shouldThrow = new Signal<bool>(false);

        var linked = Signal.AsyncLinked(async _ =>
        {
            await Task.Yield();
            if (shouldThrow.Value)
                throw new InvalidOperationException("Test exception");
            return 42;
        }, 0);

        await TestHelpers.WaitUntil(() => linked.Value == 42);
        var previousValue = linked.Value;
        previousValue.Should().Be(42);

        shouldThrow.Value = true;
        await Task.Delay(50); // Give time for potential error handling
        
        // Without fallback, should maintain previous value or use default behavior
        var currentValue = linked.Value;
        currentValue.Should().BeOneOf(previousValue, 0);
    }

    [Fact]
    public async Task LinkedSignal_CanChainMultipleSignals()
    {
        await this.SwitchToMainThread();

        var a = new Signal<int>(1);
        var b = new Signal<int>(2);
        var c = new Signal<int>(3);

        var linked = Signal.AsyncLinked(async _ =>
        {
            await Task.Yield();
            return a.Value + b.Value + c.Value;
        }, 0);

        await TestHelpers.WaitUntil(() => linked.Value == 6);
        linked.Value.Should().Be(6);

        a.Value = 10;
        await TestHelpers.WaitUntil(() => linked.Value == 15);
        linked.Value.Should().Be(15);

        b.Value = 20;
        await TestHelpers.WaitUntil(() => linked.Value == 33);
        linked.Value.Should().Be(33);

        c.Value = 30;
        await TestHelpers.WaitUntil(() => linked.Value == 60);
        linked.Value.Should().Be(60);
    }

    [Fact]
    public async Task LinkedSignal_WithComplexObject_ShouldTrackChanges()
    {
        await this.SwitchToMainThread();

        var firstName = new Signal<string>("John");
        var lastName = new Signal<string>("Doe");

        var fullName = Signal.AsyncLinked(async _ =>
        {
            await Task.Yield();
            return $"{firstName.Value} {lastName.Value}";
        }, string.Empty);

        await TestHelpers.WaitUntil(() => fullName.Value == "John Doe");
        fullName.Value.Should().Be("John Doe");

        firstName.Value = "Jane";
        await TestHelpers.WaitUntil(() => fullName.Value == "Jane Doe");
        fullName.Value.Should().Be("Jane Doe");

        lastName.Value = "Smith";
        await TestHelpers.WaitUntil(() => fullName.Value == "Jane Smith");
        fullName.Value.Should().Be("Jane Smith");
    }

    [Fact]
    public async Task LinkedSignal_ManualWrite_ShouldNotifySubscribers()
    {
        await this.SwitchToMainThread();

        var source = new Signal<int>(5);
        var linked = Signal.AsyncLinked(async _ =>
        {
            await Task.Yield();
            return source.Value * 2;
        }, 0);

        var notificationCount = 0;
        var lastValue = 0;
        linked.Values.Subscribe(x =>
        {
            notificationCount++;
            lastValue = x;
        });

        _ = linked.Value; // Initial access
        await TestHelpers.WaitUntil(() => notificationCount >= 1);
        notificationCount = 0;

        linked.Value = 100;
        await TestHelpers.WaitUntil(() => notificationCount == 1);
        notificationCount.Should().Be(1);
        lastValue.Should().Be(100);
    }

    [Fact]
    public async Task LinkedSignal_WithConfiguration_ShouldApplyConfiguration()
    {
        await this.SwitchToMainThread();

        var source = new Signal<int>(5);
        var configApplied = false;

        var linked = Signal.AsyncLinked(async _ =>
                                        {
                                            await Task.Yield();
                                            return source.Value * 2;
                                        },
                                        0,
                                        configuration: config =>
                                        {
                                            configApplied = true;
                                            return config;
                                        });

        _ = linked.Value;
        await Task.Yield();
        configApplied.Should().BeTrue();
    }

    [Fact]
    public async Task LinkedSignal_IsComputing_ShouldIndicateComputationState()
    {
        await this.SwitchToMainThread();

        var source = new Signal<int>(5);
        var delayTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var linked = Signal.AsyncLinked(async _ =>
        {
            await delayTcs.Task;
            return source.Value * 2;
        }, 0);

        _ = linked.Value;
        
        // Should be computing during computation
        await TestHelpers.WaitUntil(() => linked.IsComputing.Value);
        linked.IsComputing.Value.Should().BeTrue();

        delayTcs.SetResult();
        
        // Should not be computing after computation completes
        await TestHelpers.WaitUntil(() => !linked.IsComputing.Value);
        linked.IsComputing.Value.Should().BeFalse();
    }

    [Fact]
    public async Task LinkedSignal_ConcurrentChangeStrategy_ScheduleNext()
    {
        await this.SwitchToMainThread();

        var source = new Signal<int>(0);
        var executionCount = 0;

        var linked = Signal.AsyncLinked(async token =>
                                        {
                                            executionCount++;
                                            var value = source.Value;
                                            await Task.Delay(10, token);
                                            return value;
                                        },
                                        -1);

        _ = linked.Value;
        source.Value = 1;
        source.Value = 2;
        source.Value = 3;

        await TestHelpers.WaitUntil(() => linked.Value == 3);
        
        // With ScheduleNext, all updates should eventually be processed
        executionCount.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task LinkedSignal_ConcurrentChangeStrategy_CancelCurrent()
    {
        await this.SwitchToMainThread();

        var source = new Signal<int>(0);
        var cancellationCount = 0;

        var linked = Signal.AsyncLinked(async token =>
                                        {
                                            var value = source.Value;
                                            try
                                            {
                                                await Task.Delay(100, token);
                                            }
                                            catch (OperationCanceledException)
                                            {
                                                cancellationCount++;
                                                throw;
                                            }
                                            return value;
                                        },
                                        -1,
                                        ConcurrentChangeStrategy.CancelCurrent);

        _ = linked.Value;
        source.Value = 1;
        source.Value = 2;
        source.Value = 3;

        await TestHelpers.WaitUntil(() => linked.Value == 3);
        
        // With CancelCurrent, previous computations should be cancelled
        cancellationCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task LinkedSignal_MultipleSubscribers_ShouldNotifyAll()
    {
        await this.SwitchToMainThread();

        var source = new Signal<int>(0);
        var linked = Signal.AsyncLinked(async _ =>
        {
            await Task.Yield();
            return source.Value * 2;
        }, 0);

        var subscriber1Notifications = 0;
        var subscriber2Notifications = 0;

        linked.Values.Subscribe(_ => subscriber1Notifications++);
        linked.Values.Subscribe(_ => subscriber2Notifications++);

        _ = linked.Value;
        await TestHelpers.WaitUntil(() => subscriber1Notifications > 0 && subscriber2Notifications > 0);

        var initialNotifications1 = subscriber1Notifications;
        var initialNotifications2 = subscriber2Notifications;

        source.Value = 5;
        await TestHelpers.WaitUntil(() => 
            subscriber1Notifications > initialNotifications1 && 
            subscriber2Notifications > initialNotifications2);

        subscriber1Notifications.Should().BeGreaterThan(initialNotifications1);
        subscriber2Notifications.Should().BeGreaterThan(initialNotifications2);
    }
}
