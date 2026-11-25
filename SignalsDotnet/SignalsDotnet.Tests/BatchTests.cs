using FluentAssertions;
using R3;
using SignalsDotnet.Tests.Helpers;

namespace SignalsDotnet.Tests;

public class BatchTests
{
    [Fact]
    public async Task BatchShouldRecomputeOnlyOnceAtTheEnd()
    {
        await this.SwitchToMainThread();
        var prop1 = new Signal<int>();
        var prop2 = new Signal<int>();

        var computations = 0;
        var computed = Signal.Computed(() =>
        {
            computations++;
            return prop1.Value + prop2.Value;
        });

        computed.Values.Subscribe(_ => { });

        computations = 0;
        Signal.Batch(() =>
        {
            prop1.Value = 1;
            prop2.Value = 2;
            prop1.Value = 3;
        });

        computations.Should().Be(1);
        computed.Value.Should().Be(5);
    }

    [Fact]
    public async Task BatchShouldNotifySubscribersOnlyOnce()
    {
        await this.SwitchToMainThread();
        var prop = new Signal<int>();
        var computed = Signal.Computed(() => prop.Value);

        var notifications = 0;
        computed.Values.Subscribe(_ => notifications++);
        _ = computed.Value;

        notifications = 0;
        Signal.Batch(() =>
        {
            prop.Value = 1;
            prop.Value = 2;
            prop.Value = 3;
        });

        notifications.Should().Be(1);
        computed.Value.Should().Be(3);
    }

    [Fact]
    public async Task NestedBatchesShouldFlushOnlyOnOuterExit()
    {
        await this.SwitchToMainThread();
        var prop1 = new Signal<int>();
        var prop2 = new Signal<int>();

        var computations = 0;
        var computed = Signal.Computed(() =>
        {
            computations++;
            return prop1.Value + prop2.Value;
        });

        computed.Values.Subscribe(_ => { });

        computations = 0;
        Signal.Batch(() =>
        {
            prop1.Value = 1;
            Signal.Batch(() =>
            {
                prop2.Value = 2;
                computations.Should().Be(0);
            });

            computations.Should().Be(0);
        });

        computations.Should().Be(1);
        computed.Value.Should().Be(3);
    }

    [Fact]
    public async Task BatchShouldFlushEvenWhenActionThrows()
    {
        await this.SwitchToMainThread();
        var prop = new Signal<int>();
        var computed = Signal.Computed(() => prop.Value);
        computed.Values.Subscribe(_ => { });

        var act = () => Signal.Batch((Action)(() =>
        {
            prop.Value = 7;
            throw new InvalidOperationException();
        }));

        act.Should().Throw<InvalidOperationException>();
        computed.Value.Should().Be(7);

        prop.Value = 8;
        computed.Value.Should().Be(8);
    }

    [Fact]
    public async Task BatchShouldReturnValue()
    {
        await this.SwitchToMainThread();
        var prop = new Signal<int>();

        var result = Signal.Batch(() =>
        {
            prop.Value = 5;
            return prop.Value * 2;
        });

        result.Should().Be(10);
    }

    [Fact]
    public async Task DictionarySignalAddShouldNotifyValuesOnlyOnce()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int> { ["a"] = 1 };

        var computations = 0;
        var computed = Signal.Computed(() =>
        {
            computations++;
            return dictionary.Values.Sum();
        });

        computed.Values.Subscribe(_ => { });
        _ = computed.Value;

        computations = 0;
        dictionary["b"] = 2;

        computations.Should().Be(1);
        computed.Value.Should().Be(3);
    }

    [Fact]
    public async Task BatchedDictionaryInsertsShouldRecomputeOnlyOnce()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();

        var computations = 0;
        var computed = Signal.Computed(() =>
        {
            computations++;
            return dictionary.Values.Sum();
        });

        computed.Values.Subscribe(_ => { });
        _ = computed.Value;

        computations = 0;
        Signal.Batch(() =>
        {
            for (var i = 1; i <= 50; i++)
            {
                dictionary[$"key{i}"] = i;
            }
        });

        computations.Should().Be(1);
        computed.Value.Should().Be(50 * 51 / 2);
    }

    [Fact]
    public async Task BatchScopeShouldDeferUntilDisposed()
    {
        await this.SwitchToMainThread();
        var prop = new Signal<int>();

        var computations = 0;
        var computed = Signal.Computed(() =>
        {
            computations++;
            return prop.Value;
        });

        computed.Values.Subscribe(_ => { });

        computations = 0;
        using (Signal.BatchScope())
        {
            prop.Value = 1;
            prop.Value = 2;
            computations.Should().Be(0);
        }

        computations.Should().Be(1);
        computed.Value.Should().Be(2);
    }

    [Fact]
    public async Task NestedBatchScopesShouldFlushOnlyOnOuterDispose()
    {
        await this.SwitchToMainThread();
        var prop = new Signal<int>();

        var computations = 0;
        var computed = Signal.Computed(() =>
        {
            computations++;
            return prop.Value;
        });

        computed.Values.Subscribe(_ => { });

        computations = 0;
        using (Signal.BatchScope())
        {
            using (Signal.BatchScope())
            {
                prop.Value = 1;
            }

            computations.Should().Be(0);
            prop.Value = 2;
        }

        computations.Should().Be(1);
        computed.Value.Should().Be(2);
    }

    [Fact]
    public async Task DisposingBatchScopeTwiceShouldNotBreakBatching()
    {
        await this.SwitchToMainThread();
        var prop = new Signal<int>();

        var computations = 0;
        var computed = Signal.Computed(() =>
        {
            computations++;
            return prop.Value;
        });

        computed.Values.Subscribe(_ => { });

        var scope = Signal.BatchScope();
        prop.Value = 1;
        scope.Dispose();
        scope.Dispose();

        computations = 0;
        using (Signal.BatchScope())
        {
            prop.Value = 2;
            prop.Value = 3;
            computations.Should().Be(0);
        }

        computations.Should().Be(1);
        computed.Value.Should().Be(3);
    }

    [Fact]
    public async Task DefaultBatchReleaserShouldBeInert()
    {
        await this.SwitchToMainThread();
        var prop = new Signal<int>();

        var computations = 0;
        var computed = Signal.Computed(() =>
        {
            computations++;
            return prop.Value;
        });

        computed.Values.Subscribe(_ => { });

        default(Signal.BatchReleaserDisposable).Dispose();

        computations = 0;
        using (Signal.BatchScope())
        {
            prop.Value = 1;
            prop.Value = 2;
            computations.Should().Be(0);
        }

        computations.Should().Be(1);
        computed.Value.Should().Be(2);
    }

    [Fact(Timeout = 20_000)]
    public async Task EffectsTriggeredByOneBatchRecomputeASharedComputedOnlyOnce()
    {
        await this.SwitchToMainThread();
        var clock = new Signal<int>();
        var partials = Enumerable.Range(0, 5).Select(_ => new Signal<int>()).ToArray();
        var effects = partials.Select((partial, index) => new Effect(() =>
        {
            var tick = clock.Value;
            Signal.Batch(() => partial.Value = tick * (index + 1));
        })).ToArray();

        var computations = 0;
        var total = Signal.Computed(() =>
        {
            computations++;
            return partials.Sum(partial => partial.Value);
        });
        var totalsSeen = new List<int>();
        total.Values.Subscribe(totalsSeen.Add);

        computations = 0;
        totalsSeen.Clear();
        Signal.Batch(() => clock.Value = 1);

        computations.Should().Be(1);
        totalsSeen.Should().Equal(15);
        foreach (var effect in effects)
        {
            effect.Dispose();
        }
    }

    [Fact(Timeout = 20_000)]
    public async Task AnEffectReadingAComputedFedByAnotherEffectOfTheSameBatchEndsWithTheUpdatedValue()
    {
        await this.SwitchToMainThread();
        var clock = new Signal<int>();
        var source = new Signal<int>();
        var doubled = Signal.Computed(() => source.Value * 2);
        var observed = new Signal<int>();
        using var writer = new Effect(() =>
        {
            var tick = clock.Value;
            Signal.Batch(() => source.Value = tick);
        });
        using var reader = new Effect(() =>
        {
            var value = clock.Value + doubled.Value;
            Signal.Batch(() => observed.Value = value);
        });

        Signal.Batch(() => clock.Value = 3);

        doubled.Value.Should().Be(6);
        observed.Value.Should().Be(9);
    }

    [Fact(Timeout = 20_000)]
    public async Task AChainOfComputedsRecomputesEachLevelOnceWhenTriggeredByABatchedEffect()
    {
        await this.SwitchToMainThread();
        var clock = new Signal<int>();
        var source = new Signal<int>();
        using var writer = new Effect(() =>
        {
            var tick = clock.Value;
            Signal.Batch(() => source.Value = tick);
        });

        var firstLevelComputations = 0;
        var secondLevelComputations = 0;
        var firstLevel = Signal.Computed(() =>
        {
            firstLevelComputations++;
            return source.Value + 1;
        });
        var secondLevel = Signal.Computed(() =>
        {
            secondLevelComputations++;
            return firstLevel.Value * 10;
        });
        secondLevel.Values.Subscribe(_ => { });

        firstLevelComputations = 0;
        secondLevelComputations = 0;
        Signal.Batch(() => clock.Value = 4);

        firstLevelComputations.Should().Be(1);
        secondLevelComputations.Should().Be(1);
        secondLevel.Value.Should().Be(50);
    }

    [Fact]
    public async Task BatchShouldStillTrackNewlyAddedDependencies()
    {
        await this.SwitchToMainThread();
        var useSecond = new Signal<bool>();
        var first = new Signal<int>(1);
        var second = new Signal<int>(2);

        var computed = Signal.Computed(() => useSecond.Value ? second.Value : first.Value);
        computed.Values.Subscribe(_ => { });
        computed.Value.Should().Be(1);

        Signal.Batch(() => useSecond.Value = true);
        computed.Value.Should().Be(2);

        second.Value = 5;
        computed.Value.Should().Be(5);
    }
}
