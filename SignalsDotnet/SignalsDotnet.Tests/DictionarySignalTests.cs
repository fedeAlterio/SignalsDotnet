using FluentAssertions;
using SignalsDotnet.Tests.Helpers;

namespace SignalsDotnet.Tests;

public class DictionarySignalTests
{
    [Fact]
    public async Task ShouldNotifyWhenValueChanges()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<int, string>();

        var disconnect = new Signal<bool>();
        var invocationsCount = 0;
        var computed = Signal.Computed(() =>
        {
            invocationsCount++;
            if (disconnect.Value) return "disconnected";
            var value = dictionary.TryGetValue(1, out var val) ? val : "unknown";
            return value;
        });
        _ = computed.Value;
        invocationsCount.Should().Be(1);
        dictionary.KeySignals.Count.Should().Be(1);

        dictionary[1] = "BBB";
        invocationsCount.Should().Be(2);
        computed.Value.Should().Be("BBB");

        dictionary.Remove(1);
        invocationsCount.Should().Be(3);
        computed.Value.Should().Be("unknown");

        disconnect.Value = true;
        invocationsCount.Should().Be(4);
        computed.Value.Should().Be("disconnected");
        dictionary.KeySignals.Count.Should().Be(0);

        dictionary[1] = "CCC";
        invocationsCount.Should().Be(4);

        disconnect.Value = false;
        invocationsCount.Should().Be(5);
        computed.Value.Should().Be("CCC");
        dictionary.KeySignals.Count.Should().Be(1);
        disconnect.Value = true;
        dictionary.KeySignals.Count.Should().Be(0);
    }

    [Fact]
    public async Task ShouldTrackMultipleKeys()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;
        dictionary["b"] = 2;
        dictionary["c"] = 3;

        var invocationsCount = 0;
        var computed = Signal.Computed(() =>
        {
            invocationsCount++;
            return dictionary["a"] + dictionary["b"];
        });

        _ = computed.Value;
        invocationsCount.Should().Be(1);
        computed.Value.Should().Be(3);
        dictionary.KeySignals.Count.Should().Be(2);

        dictionary["a"] = 10;
        invocationsCount.Should().Be(2);
        computed.Value.Should().Be(12);

        dictionary["b"] = 20;
        invocationsCount.Should().Be(3);
        computed.Value.Should().Be(30);

        dictionary["c"] = 100;
        invocationsCount.Should().Be(3);
        computed.Value.Should().Be(30);
    }

    [Fact]
    public async Task ShouldNotifyOnKeysCollectionAccess()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;
        dictionary["b"] = 2;

        var invocationsCount = 0;
        var computed = Signal.Computed(() =>
        {
            invocationsCount++;
            return dictionary.Keys.Count;
        });

        _ = computed.Value;
        invocationsCount.Should().Be(1);
        computed.Value.Should().Be(2);

        dictionary["c"] = 3;
        invocationsCount.Should().Be(2);
        computed.Value.Should().Be(3);

        dictionary.Remove("a");
        invocationsCount.Should().Be(3);
        computed.Value.Should().Be(2);
    }

    [Fact]
    public async Task ShouldNotifyOnValuesCollectionAccess()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;
        dictionary["b"] = 2;

        var invocationsCount = 0;
        var computed = Signal.Computed(() =>
        {
            invocationsCount++;
            return dictionary.Values.Sum();
        });

        _ = computed.Value;
        invocationsCount.Should().Be(1);
        computed.Value.Should().Be(3);

        dictionary["a"] = 10;
        invocationsCount.Should().Be(2);
        computed.Value.Should().Be(12);

        dictionary["c"] = 5;
        computed.Value.Should().Be(17);

        dictionary.Remove("b");
        computed.Value.Should().Be(15);
    }

    [Fact]
    public async Task ShouldNotifyOnCountAccess()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();

        var invocationsCount = 0;
        var computed = Signal.Computed(() =>
        {
            invocationsCount++;
            return dictionary.Count;
        });

        _ = computed.Value;
        invocationsCount.Should().Be(1);
        computed.Value.Should().Be(0);

        dictionary["a"] = 1;
        invocationsCount.Should().Be(2);
        computed.Value.Should().Be(1);

        dictionary["b"] = 2;
        invocationsCount.Should().Be(3);
        computed.Value.Should().Be(2);

        dictionary.Remove("a");
        invocationsCount.Should().Be(4);
        computed.Value.Should().Be(1);
    }

    [Fact]
    public async Task ShouldNotifyOnContainsKey()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;

        var invocationsCount = 0;
        var computed = Signal.Computed(() =>
        {
            invocationsCount++;
            return dictionary.ContainsKey("b");
        });

        _ = computed.Value;
        invocationsCount.Should().Be(1);
        computed.Value.Should().BeFalse();

        dictionary["b"] = 2;
        invocationsCount.Should().Be(2);
        computed.Value.Should().BeTrue();

        dictionary.Remove("b");
        invocationsCount.Should().Be(3);
        computed.Value.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldHandleClear()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;
        dictionary["b"] = 2;

        var invocationsCount = 0;
        var computed = Signal.Computed(() =>
        {
            invocationsCount++;
            return dictionary.Count;
        });

        _ = computed.Value;
        invocationsCount.Should().Be(1);
        computed.Value.Should().Be(2);

        dictionary.Clear();
        invocationsCount.Should().Be(2);
        computed.Value.Should().Be(0);
    }

    [Fact]
    public async Task ShouldHandleMultipleComputedsDependingOnSameKey()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();
        dictionary["shared"] = 1;

        var invocations1 = 0;
        var computed1 = Signal.Computed(() =>
        {
            invocations1++;
            return dictionary["shared"] * 2;
        });

        var invocations2 = 0;
        var computed2 = Signal.Computed(() =>
        {
            invocations2++;
            return dictionary["shared"] * 3;
        });

        _ = computed1.Value;
        _ = computed2.Value;
        invocations1.Should().Be(1);
        invocations2.Should().Be(1);

        dictionary["shared"] = 5;
        invocations1.Should().Be(2);
        invocations2.Should().Be(2);
        computed1.Value.Should().Be(10);
        computed2.Value.Should().Be(15);
    }

    [Fact]
    public async Task ShouldHandleIndexerSet_OnExistingKey()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;

        var invocationsCount = 0;
        var computed = Signal.Computed(() =>
        {
            invocationsCount++;
            return dictionary["a"];
        });

        _ = computed.Value;
        invocationsCount.Should().Be(1);
        computed.Value.Should().Be(1);

        dictionary["a"] = 10;
        invocationsCount.Should().Be(2);
        computed.Value.Should().Be(10);
    }

    [Fact]
    public async Task ShouldHandleIndexerSet_OnNewKey()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();

        var invocationsCount = 0;
        var computed = Signal.Computed(() =>
        {
            invocationsCount++;
            return dictionary.TryGetValue("a", out var val) ? val : -1;
        });

        _ = computed.Value;
        invocationsCount.Should().Be(1);
        computed.Value.Should().Be(-1);

        dictionary["a"] = 100;
        invocationsCount.Should().Be(2);
        computed.Value.Should().Be(100);
    }

    [Fact]
    public async Task ShouldHandleAddMethod()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();

        var invocationsCount = 0;
        var computed = Signal.Computed(() =>
        {
            invocationsCount++;
            return dictionary.Count;
        });

        _ = computed.Value;
        invocationsCount.Should().Be(1);
        computed.Value.Should().Be(0);

        dictionary.Add("a", 1);
        invocationsCount.Should().Be(2);
        computed.Value.Should().Be(1);
    }

    [Fact]
    public async Task ShouldHandleContainsKeyValuePair()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;

        var invocationsCount = 0;
        var computed = Signal.Computed(() =>
        {
            invocationsCount++;
            return dictionary.Contains(new KeyValuePair<string, int>("a", 1));
        });

        _ = computed.Value;
        invocationsCount.Should().Be(1);
        computed.Value.Should().BeTrue();

        dictionary["a"] = 2;
        invocationsCount.Should().Be(2);
        computed.Value.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldHandleRemoveKeyValuePair()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;
        dictionary["b"] = 2;

        var invocationsCount = 0;
        var computed = Signal.Computed(() =>
        {
            invocationsCount++;
            return dictionary.Count;
        });

        _ = computed.Value;
        invocationsCount.Should().Be(1);

        dictionary.Remove(new KeyValuePair<string, int>("a", 1));
        invocationsCount.Should().Be(2);
        computed.Value.Should().Be(1);
    }

    [Fact]
    public void ShouldHandleCopyTo()
    {
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;
        dictionary["b"] = 2;

        var array = new KeyValuePair<string, int>[2];
        dictionary.CopyTo(array, 0);

        array.Should().Contain(new KeyValuePair<string, int>("a", 1));
        array.Should().Contain(new KeyValuePair<string, int>("b", 2));
    }

    [Fact]
    public void ValuesCollection_ShouldHandleContains()
    {
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;
        dictionary["b"] = 2;

        dictionary.Values.Contains(1).Should().BeTrue();
        dictionary.Values.Contains(3).Should().BeFalse();
    }

    [Fact]
    public void ValuesCollection_ShouldHandleCopyTo()
    {
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;
        dictionary["b"] = 2;

        var array = new int[2];
        dictionary.Values.CopyTo(array, 0);

        array.Should().Contain(1);
        array.Should().Contain(2);
    }

    [Fact]
    public void ShouldEnumerateKeyValuePairs()
    {
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;
        dictionary["b"] = 2;

        var list = dictionary.ToList();
        list.Should().HaveCount(2);
        list.Should().Contain(new KeyValuePair<string, int>("a", 1));
        list.Should().Contain(new KeyValuePair<string, int>("b", 2));
    }

    [Fact]
    public async Task ShouldCleanupKeySignalsWhenNoLongerTracked()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>
        {
            ["a"] = 1,
            ["b"] = 2
        };

        var useA = new Signal<bool>(true);
        var computed = Signal.Computed(() => useA.Value ? dictionary["a"] : dictionary["b"]);

        _ = computed.Value;
        dictionary.KeySignals.Count.Should().Be(1);

        useA.Value = false;
        dictionary.KeySignals.Count.Should().Be(1);

        useA.Value = true;
        dictionary.KeySignals.Count.Should().Be(1);
    }

    [Fact]
    public async Task Clear_ShouldNotifyAllTrackedKeys()
    {
        await this.SwitchToMainThread();
        
        var dictionary = new DictionarySignal<string, int>();
        dictionary["key1"] = 1;
        dictionary["key2"] = 1;

        var computed1 = Signal.Computed(() => dictionary.ContainsKey("key1"));
        var computed2 = Signal.Computed(() => dictionary.ContainsKey("key2"));
        _ = computed1.Value;
        _ = computed2.Value;
        computed1.Value.Should().BeTrue();
        computed2.Value.Should().BeTrue();
        dictionary.Clear();
        computed1.Value.Should().BeFalse();
        computed2.Value.Should().BeFalse();
    }
}
