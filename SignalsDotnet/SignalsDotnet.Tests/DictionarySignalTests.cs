using FluentAssertions;
using R3;
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

    [Fact]
    public async Task KeyAdded_ShouldFireOnIndexerSet_ForNewKey()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();

        var addedKeys = new List<string>();
        dictionary.KeyAdded.Subscribe(x => addedKeys.Add(x.key));

        dictionary["a"] = 1;
        addedKeys.Should().Equal("a");
    }

    [Fact]
    public async Task KeyAdded_ShouldFireOnAddMethod()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();

        var addedKeys = new List<string>();
        dictionary.KeyAdded.Subscribe(x => addedKeys.Add(x.key));

        dictionary.Add("a", 1);
        addedKeys.Should().Equal("a");
    }

    [Fact]
    public async Task KeyAdded_ShouldNotFireWhenSettingExistingKey()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;

        var addedKeys = new List<string>();
        dictionary.KeyAdded.Subscribe(x => addedKeys.Add(x.key));

        dictionary["a"] = 2;
        addedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task KeyAdded_ShouldNotFireForKeysAddedBeforeSubscription()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;

        var addedKeys = new List<string>();
        dictionary.KeyAdded.Subscribe(x => addedKeys.Add(x.key));

        addedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task KeyAdded_Signal_ShouldStartTrueAndBecomeFalseOnRemove()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();

        IReadOnlySignal<bool>? isInDictionary = null;
        dictionary.KeyAdded.Subscribe(x => isInDictionary = x.isInDictionary);

        dictionary["a"] = 1;
        isInDictionary.Should().NotBeNull();

        var values = new List<bool>();
        isInDictionary!.Values.Subscribe(values.Add);
        values.Should().Equal(true);

        dictionary.Remove("a");
        values.Should().Equal(true, false);
    }

    [Fact]
    public async Task KeyAdded_Signal_ShouldBecomeFalseOnClear()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();

        IReadOnlySignal<bool>? isInDictionary = null;
        dictionary.KeyAdded.Subscribe(x => isInDictionary = x.isInDictionary);

        dictionary["a"] = 1;

        var values = new List<bool>();
        isInDictionary!.Values.Subscribe(values.Add);
        values.Should().Equal(true);

        dictionary.Clear();
        values.Should().Equal(true, false);
    }

    [Fact]
    public async Task KeyAdded_Signal_ShouldDetachAfterBecomingFalse()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();

        IReadOnlySignal<bool>? isInDictionary = null;
        dictionary.KeyAdded.Subscribe(x => isInDictionary = x.isInDictionary);

        dictionary["a"] = 1;

        var values = new List<bool>();
        isInDictionary!.Values.Subscribe(values.Add);
        values.Should().Equal(true);

        dictionary.Remove("a");
        values.Should().Equal(true, false);

        dictionary["a"] = 2;
        values.Should().Equal(true, false);
    }

    [Fact]
    public async Task KeyAdded_ShouldFireAgainAndTrackIndependently_OnAddRemoveAddRemove()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();

        var addedKeys = new List<string>();
        var presenceSignals = new List<IReadOnlySignal<bool>>();
        var liveValuesPerSignal = new List<List<bool>>();
        dictionary.KeyAdded.Subscribe(x =>
        {
            addedKeys.Add(x.key);
            presenceSignals.Add(x.isInDictionary);

            var liveValues = new List<bool>();
            x.isInDictionary.Values.Subscribe(liveValues.Add);
            liveValuesPerSignal.Add(liveValues);
        });

        dictionary["a"] = 1;
        dictionary.Remove("a");
        dictionary["a"] = 2;

        presenceSignals[0].Value.Should().BeFalse();
        presenceSignals[1].Value.Should().BeTrue();

        dictionary.Remove("a");

        addedKeys.Should().Equal("a", "a");
        presenceSignals.Should().HaveCount(2);

        liveValuesPerSignal[0].Should().Equal(true, false);
        liveValuesPerSignal[1].Should().Equal(true, false);

        presenceSignals[0].Value.Should().BeFalse();
        presenceSignals[1].Value.Should().BeFalse();
    }

    [Fact]
    public async Task KeyAddedIncludingCurrent_ShouldEmitExistingKeysOnSubscribe()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;
        dictionary["b"] = 2;

        var addedKeys = new List<string>();
        dictionary.KeyAddedIncludingCurrent().Subscribe(x => addedKeys.Add(x.key));

        addedKeys.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public async Task KeyAddedIncludingCurrent_ShouldAlsoEmitFutureKeys()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;

        var addedKeys = new List<string>();
        dictionary.KeyAddedIncludingCurrent().Subscribe(x => addedKeys.Add(x.key));
        addedKeys.Should().Equal("a");

        dictionary["b"] = 2;
        addedKeys.Should().Equal("a", "b");
    }

    [Fact]
    public async Task KeyAddedIncludingCurrent_PresenceSignalForExistingKey_ShouldTrackRemoval()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;

        IReadOnlySignal<bool>? isInDictionary = null;
        dictionary.KeyAddedIncludingCurrent().Subscribe(x => isInDictionary = x.isInDictionary);

        isInDictionary!.Value.Should().BeTrue();

        dictionary.Remove("a");
        isInDictionary.Value.Should().BeFalse();
    }

    [Fact]
    public async Task KeyAddedIncludingCurrent_ShouldNotDropKeyAddedReentrantlyDuringSnapshot()
    {
        await this.SwitchToMainThread();
        var dictionary = new DictionarySignal<string, int>();
        dictionary["a"] = 1;

        var addedKeys = new List<string>();
        dictionary.KeyAddedIncludingCurrent().Subscribe(x =>
        {
            addedKeys.Add(x.key);
            if (x.key == "a")
            {
                dictionary["b"] = 2;
            }
        });

        addedKeys.Should().Equal("a", "b");
    }
}
