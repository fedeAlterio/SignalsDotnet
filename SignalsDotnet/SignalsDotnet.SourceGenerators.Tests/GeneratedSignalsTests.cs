using System.ComponentModel;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using R3;
using SignalsDotnet.SourceGenerators.Tests.Helpers;

namespace SignalsDotnet.SourceGenerators.Tests;

public class GeneratedSignalsTests
{
    const int TestTimeoutMs = 10_000;

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Property_get_and_set_round_trip()
    {
        await this.SwitchToMainThread();
        var person = new Person();

        person.Name.Should().BeNull();
        person.Age.Should().Be(0);

        person.Name = "Ada";
        person.Age = 36;

        person.Name.Should().Be("Ada");
        person.Age.Should().Be(36);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Property_setter_writes_through_to_the_signal()
    {
        await this.SwitchToMainThread();
        var person = new Person();

        person.Name = "Ada";

        person.NameSignal.Value.Should().Be("Ada");
        person.NameSignal.UntrackedValue.Should().Be("Ada");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Signal_emits_values_when_the_property_changes()
    {
        await this.SwitchToMainThread();
        var person = new Person();
        var seen = new List<string?>();

        using var subscription = person.NameSignal.Values.Subscribe(x => seen.Add(x));

        person.Name = "Ada";
        person.Name = "Grace";

        seen.Should().Equal(null, "Ada", "Grace");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Signal_does_not_emit_when_the_value_is_unchanged()
    {
        await this.SwitchToMainThread();
        var person = new Person();
        var seen = new List<int>();

        using var subscription = person.AgeSignal.Values.Subscribe(x => seen.Add(x));

        person.Age = 36;
        person.Age = 36;

        seen.Should().Equal(0, 36);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Generated_properties_participate_in_computed_signals()
    {
        await this.SwitchToMainThread();
        var person = new Person { Name = "Ada", Age = 36 };
        var description = Signal.Computed(() => $"{person.Name} ({person.Age})");

        description.Value.Should().Be("Ada (36)");

        person.Age = 37;
        description.Value.Should().Be("Ada (37)");

        person.Name = "Grace";
        description.Value.Should().Be("Grace (37)");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Computed_signal_only_tracks_the_properties_it_reads()
    {
        await this.SwitchToMainThread();
        var person = new Person { Name = "Ada", Age = 36 };
        var computations = 0;

        var upper = Signal.Computed(() =>
        {
            computations++;
            return person.Name.ToUpperInvariant();
        });

        upper.Value.Should().Be("ADA");
        var afterFirstRead = computations;

        person.Age = 99;

        upper.Value.Should().Be("ADA");
        computations.Should().Be(afterFirstRead);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Ignored_properties_are_not_backed_by_a_signal()
    {
        await this.SwitchToMainThread();
        var model = new WithIgnored();

        typeof(WithIgnored).GetProperty("TrackedSignal").Should().NotBeNull();
        typeof(WithIgnored).GetProperty("NotTrackedSignal").Should().BeNull();
        typeof(WithIgnored).GetProperty("ComputedSignal").Should().BeNull();

        model.Tracked = "x";
        model.Computed.Should().Be("x!");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Computed_property_reflects_its_dependencies()
    {
        await this.SwitchToMainThread();
        var person = new Person { Name = "Ada", Age = 36 };

        person.FullName.Should().Be("Ada 36");

        person.Age = 37;
        person.FullName.Should().Be("Ada 37");

        person.Name = "Grace";
        person.FullName.Should().Be("Grace 37");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Computed_signal_emits_on_dependency_changes()
    {
        await this.SwitchToMainThread();
        var person = new Person { Name = "Ada", Age = 36 };
        var seen = new List<string>();

        using var subscription = person.FullNameSignal.Values.Subscribe(x => seen.Add(x));

        person.Age = 37;
        person.Name = "Grace";

        seen.Should().Equal("Ada 36", "Ada 37", "Grace 37");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Computed_property_is_read_only()
    {
        await this.SwitchToMainThread();
        typeof(Person).GetProperty(nameof(Person.FullName))!.CanWrite.Should().BeFalse();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Computed_works_when_declared_before_its_dependency()
    {
        await this.SwitchToMainThread();
        var model = new ComputedDeclaredFirst { Name = "ada" };

        model.Upper.Should().Be("ADA");

        model.Name = "grace";
        model.Upper.Should().Be("GRACE");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Computed_can_depend_on_another_computed()
    {
        await this.SwitchToMainThread();
        var model = new ChainedComputed { Value = 2 };

        model.Doubled.Should().Be(4);
        model.Quadrupled.Should().Be(8);
        model.Described.Should().Be("2 -> 8");

        model.Value = 5;

        model.Doubled.Should().Be(10);
        model.Quadrupled.Should().Be(20);
        model.Described.Should().Be("5 -> 20");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Computed_property_raises_PropertyChanged_when_a_dependency_changes()
    {
        await this.SwitchToMainThread();
        var person = new Person { Name = "Ada", Age = 36 };
        var raised = new List<string?>();

        using var keepComputedAlive = person.FullNameSignal.Values.Subscribe(_ => { });
        ((INotifyPropertyChanged)person).PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        person.Age = 37;

        raised.Should().Contain(nameof(Person.FullName));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Computed_property_does_not_raise_PropertyChanged_when_its_value_is_unchanged()
    {
        await this.SwitchToMainThread();
        var model = new ChainedComputed { Value = 2 };
        var raised = new List<string?>();

        ((INotifyPropertyChanged)model).PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        model.Value = 2;

        raised.Should().NotContain(nameof(ChainedComputed.Doubled));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Async_computed_property_resolves_its_value()
    {
        await this.SwitchToMainThread();
        var person = new AsyncPerson { Name = "Ada" };

        using var subscription = person.GreetingSignal.Values.Subscribe(_ => { });

        await TestHelpers.WaitUntil(() => person.Greeting == "Hello Ada");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Async_computed_property_recomputes_when_a_dependency_changes()
    {
        await this.SwitchToMainThread();
        var person = new AsyncPerson { Name = "Ada" };

        using var subscription = person.GreetingSignal.Values.Subscribe(_ => { });

        await TestHelpers.WaitUntil(() => person.Greeting == "Hello Ada");

        person.Name = "Grace";

        await TestHelpers.WaitUntil(() => person.Greeting == "Hello Grace");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Async_computed_raises_PropertyChanged_when_the_value_arrives()
    {
        await this.SwitchToMainThread();
        var person = new AsyncPerson { Name = "Ada" };
        var raised = new List<string?>();

        using var keepComputedAlive = person.GreetingSignal.Values.Subscribe(_ => { });
        ((INotifyPropertyChanged)person).PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        await TestHelpers.WaitUntil(() => raised.Contains(nameof(AsyncPerson.Greeting)));

        person.Greeting.Should().Be("Hello Ada");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Async_computed_supports_Task_returning_methods()
    {
        await this.SwitchToMainThread();
        var model = new AsyncWithTask { Value = 21 };

        using var subscription = model.DoubledSignal.Values.Subscribe(_ => { });

        await TestHelpers.WaitUntil(() => model.Doubled == 42);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Async_computed_exposes_an_IsComputing_signal()
    {
        await this.SwitchToMainThread();
        var person = new AsyncPerson();

        typeof(AsyncPerson).GetProperty(nameof(AsyncPerson.IsGreetingComputing))!
                           .PropertyType.Should().Be(typeof(bool));

        person.GreetingSignal.Should().BeAssignableTo<IAsyncReadOnlySignal<string>>();
        person.GreetingSignal.IsComputing.Should().NotBeNull();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Signal_attribute_backs_a_single_property_without_the_class_attribute()
    {
        await this.SwitchToMainThread();
        var model = new PerPropertyOptIn();

        model.Tracked = "x";

        model.TrackedSignal.Value.Should().Be("x");
        typeof(PerPropertyOptIn).GetProperty("PlainSignal").Should().BeNull();
        model.Should().BeAssignableTo<INotifyPropertyChanged>();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Signal_attribute_composes_with_computed_methods()
    {
        await this.SwitchToMainThread();
        var model = new PerPropertyWithComputed { Name = "ada" };

        model.Shout.Should().Be("ADA");

        model.Name = "grace";
        model.Shout.Should().Be("GRACE");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task PropertyChanged_is_raised_when_the_signal_is_written_directly()
    {
        await this.SwitchToMainThread();
        var person = new Person();
        var raised = new List<string?>();

        ((INotifyPropertyChanged)person).PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        ((Signal<string>)person.NameSignal).Value = "Ada";

        raised.Should().Contain(nameof(Person.Name));
        person.Name.Should().Be("Ada");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Custom_constructor_can_call_InitializeSignals_and_use_signals_immediately_after()
    {
        await this.SwitchToMainThread();
        var model = new WithInitializationHook();

        model.Name.Should().Be("from hook");
        model.ShoutAtInitialization.Should().Be("FROM HOOK");
        model.Shout.Should().Be("FROM HOOK");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Parameterized_constructor_can_call_InitializeSignals()
    {
        await this.SwitchToMainThread();
        var model = new WithParameterizedConstructor("ada");

        model.Name.Should().Be("ada");
        model.Shout.Should().Be("ADA");

        model.Name = "grace";
        model.Shout.Should().Be("GRACE");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Constructor_chaining_with_this_initializes_the_signals()
    {
        await this.SwitchToMainThread();
        var model = new WithChainedConstructor(0);

        model.Name.Should().Be("chained");
        model.Shout.Should().Be("CHAINED");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Parameterized_constructor_only_does_not_get_a_generated_parameterless_one()
    {
        await this.SwitchToMainThread();

        typeof(WithParameterizedConstructor).GetConstructor(global::System.Type.EmptyTypes)
                                            .Should().BeNull();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task InitializeSignals_is_generated_even_without_any_signal_member()
    {
        await this.SwitchToMainThread();

        typeof(WithoutAnySignalMember).GetMethod("InitializeSignals",
                                                 BindingFlags.Instance | BindingFlags.NonPublic)
                                      .Should().NotBeNull();

        var model = new WithoutAnySignalMember();
        model.Should().NotBeNull();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Constructor_can_call_InitializeSignals_without_any_signal_member()
    {
        await this.SwitchToMainThread();
        var model = new WithoutSignalsButWithConstructor("ada");

        model.Marker.Should().Be("ada");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task IsComputing_property_tracks_the_running_state()
    {
        await this.SwitchToMainThread();
        var person = new AsyncPerson { Name = "Ada" };

        using var subscription = person.GreetingSignal.Values.Subscribe(_ => { });

        await TestHelpers.WaitUntil(() => person.Greeting == "Hello Ada");

        person.IsGreetingComputing.Should().BeFalse();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task IsComputing_raises_PropertyChanged()
    {
        await this.SwitchToMainThread();
        var person = new AsyncPerson { Name = "Ada" };
        var raised = new List<string?>();

        using var subscription = person.GreetingSignal.Values.Subscribe(_ => { });
        ((INotifyPropertyChanged)person).PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        await TestHelpers.WaitUntil(() => raised.Contains(nameof(AsyncPerson.IsGreetingComputing)));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task INotifyPropertyChanged_is_not_generated_without_the_attribute()
    {
        await this.SwitchToMainThread();
        var model = new WithoutNotification { Name = "ada" };

        model.Should().NotBeAssignableTo<INotifyPropertyChanged>();
        model.Shout.Should().Be("ADA");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task INotifyPropertyChanged_can_be_disabled_explicitly()
    {
        await this.SwitchToMainThread();
        var model = new NotificationDisabled();

        model.Should().NotBeAssignableTo<INotifyPropertyChanged>();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task INotifyPropertyChanged_is_implemented_when_requested()
    {
        await this.SwitchToMainThread();
        var person = new Person();
        var raised = new List<string?>();

        person.Should().BeAssignableTo<INotifyPropertyChanged>();
        ((INotifyPropertyChanged)person).PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        person.Name = "Ada";
        person.Age = 36;

        raised.Should().Contain(nameof(Person.Name));
        raised.Should().Contain(nameof(Person.Age));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Nested_and_generic_classes_are_supported()
    {
        await this.SwitchToMainThread();
        var nested = new Outer.Nested { Name = "Ada" };
        nested.NameSignal.Value.Should().Be("Ada");

        var generic = new Generic<Person>();
        var person = new Person();
        generic.Item = person;
        generic.ItemSignal.Value.Should().BeSameAs(person);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Signal_accessor_mirrors_the_setter_accessibility_of_the_property()
    {
        await this.SwitchToMainThread();
        var model = new WithAccessors();

        typeof(WithAccessors).GetProperty("PrivateSetter")!.SetMethod!.IsPrivate.Should().BeTrue();
        model.PrivateSetterSignal.Should().NotBeNull();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ModelChanged_is_raised_when_any_signal_property_changes()
    {
        await this.SwitchToMainThread();
        var person = new Person();
        var raised = 0;

        using var subscription = person.ModelChanged.Values.Subscribe(_ => raised++);
        var initial = raised;

        person.Name = "Ada";
        raised.Should().Be(initial + 1);

        person.Age = 36;
        raised.Should().Be(initial + 2);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ModelChanged_follows_the_deduplication_of_the_source_signal()
    {
        await this.SwitchToMainThread();
        var person = new Person { Name = "Ada" };
        var raised = 0;

        using var subscription = person.ModelChanged.Values.Subscribe(_ => raised++);
        var initial = raised;

        person.Name = "Ada";
        raised.Should().Be(initial);

        person.Name = "Grace";
        raised.Should().Be(initial + 1);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ModelChanged_does_not_react_to_computed_properties()
    {
        await this.SwitchToMainThread();
        var model = new ChainedComputed();
        var raised = 0;

        using var keepComputedAlive = model.DoubledSignal.Values.Subscribe(_ => { });
        using var subscription = model.ModelChanged.Values.Subscribe(_ => raised++);
        var initial = raised;

        model.Value = 5;

        raised.Should().Be(initial + 1);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ModelChanged_participates_in_computed_signals()
    {
        await this.SwitchToMainThread();
        var person = new Person();
        var revisions = 0;

        var revision = Signal.Computed(() =>
        {
            _ = person.ModelChanged.Value;
            return ++revisions;
        });

        using var subscription = revision.Values.Subscribe(_ => { });

        var afterFirstChange = 0;

        person.Name = "Ada";
        afterFirstChange = revision.Value;
        afterFirstChange.Should().BeGreaterThan(0);

        person.Age = 36;
        revision.Value.Should().BeGreaterThan(afterFirstChange);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ModelChanged_is_a_signal_of_the_model()
    {
        await this.SwitchToMainThread();
        var person = new Person();

        person.ModelChanged.Should().BeAssignableTo<IReadOnlySignal<Person>>();
        typeof(Person).GetProperty(nameof(Person.ModelChanged))!.CanWrite.Should().BeFalse();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ModelChanged_yields_the_model_instance()
    {
        await this.SwitchToMainThread();
        var person = new Person();
        var seen = new List<Person>();

        using var subscription = person.ModelChanged.Values.Subscribe(x => seen.Add(x));

        person.Name = "Ada";

        seen.Should().NotBeEmpty();
        seen.Should().AllSatisfy(x => x.Should().BeSameAs(person));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ModelChanged_is_not_generated_without_signal_properties()
    {
        await this.SwitchToMainThread();

        typeof(OnlyComputed).GetProperty("ModelChanged").Should().BeNull();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Model_round_trips_through_System_Text_Json()
    {
        await this.SwitchToMainThread();
        var person = new Person { Name = "Ada", Age = 36 };

        var json = JsonSerializer.Serialize(person);
        var restored = JsonSerializer.Deserialize<Person>(json)!;

        restored.Name.Should().Be("Ada");
        restored.Age.Should().Be(36);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Serialized_json_only_contains_the_data_properties()
    {
        await this.SwitchToMainThread();
        var person = new Person { Name = "Ada", Age = 36 };

        var json = JsonSerializer.Serialize(person);
        using var document = JsonDocument.Parse(json);

        var names = document.RootElement.EnumerateObject().Select(x => x.Name).ToArray();

        names.Should().BeEquivalentTo(nameof(Person.Name), nameof(Person.Age), nameof(Person.FullName));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Signal_members_are_ignored_on_serialization()
    {
        await this.SwitchToMainThread();

        AssertIgnored<Person>(nameof(Person.NameSignal));
        AssertIgnored<Person>(nameof(Person.AgeSignal));
        AssertIgnored<Person>(nameof(Person.FullNameSignal));
        AssertIgnored<Person>(nameof(Person.ModelChanged));

        AssertIgnored<AsyncPerson>(nameof(AsyncPerson.GreetingSignal));
        AssertIgnored<AsyncPerson>(nameof(AsyncPerson.IsGreetingComputing));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Computed_values_are_serialized()
    {
        await this.SwitchToMainThread();

        AssertNotIgnored<Person>(nameof(Person.FullName));
        AssertNotIgnored<AsyncPerson>(nameof(AsyncPerson.Greeting));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Async_model_round_trips_through_System_Text_Json()
    {
        await this.SwitchToMainThread();
        var person = new AsyncPerson { Name = "Ada" };

        var json = JsonSerializer.Serialize(person);
        var restored = JsonSerializer.Deserialize<AsyncPerson>(json)!;

        restored.Name.Should().Be("Ada");
    }

    static void AssertIgnored<T>(string propertyName)
    {
        var property = typeof(T).GetProperty(propertyName);
        property.Should().NotBeNull($"{propertyName} should exist on {typeof(T).Name}");

        property!.GetCustomAttribute<JsonIgnoreAttribute>()
                 .Should().NotBeNull($"{propertyName} should carry [JsonIgnore]");

        property!.GetCustomAttribute<IgnoreDataMemberAttribute>()
                 .Should().NotBeNull($"{propertyName} should carry [IgnoreDataMember]");
    }

    static void AssertNotIgnored<T>(string propertyName)
    {
        var property = typeof(T).GetProperty(propertyName);
        property.Should().NotBeNull($"{propertyName} should exist on {typeof(T).Name}");

        property!.GetCustomAttribute<JsonIgnoreAttribute>()
                 .Should().BeNull($"{propertyName} should be queryable and serialized");

        property!.GetCustomAttribute<IgnoreDataMemberAttribute>()
                 .Should().BeNull($"{propertyName} should be queryable and serialized");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Record_properties_round_trip()
    {
        await this.SwitchToMainThread();
        var person = new PersonRecord { Name = "Ada", Age = 36 };

        person.Name.Should().Be("Ada");
        person.Age.Should().Be(36);
        person.NameSignal.Value.Should().Be("Ada");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Record_supports_computed_properties_and_notification()
    {
        await this.SwitchToMainThread();
        var person = new PersonRecord { Name = "Ada", Age = 36 };
        var raised = new List<string?>();

        using var keepAlive = person.FullNameSignal.Values.Subscribe(_ => { });
        ((INotifyPropertyChanged)person).PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        person.FullName.Should().Be("Ada 36");

        person.Age = 37;

        person.FullName.Should().Be("Ada 37");
        raised.Should().Contain(nameof(PersonRecord.Age));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Record_struct_is_supported_without_ModelChanged()
    {
        await this.SwitchToMainThread();
        var point = new PointRecordStruct { X = 3 };

        point.X.Should().Be(3);
        typeof(PointRecordStruct).GetProperty("ModelChanged").Should().BeNull();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Nested_records_are_supported()
    {
        await this.SwitchToMainThread();
        var nested = new Container.NestedRecord { Name = "Ada" };

        nested.NameSignal.Value.Should().Be("Ada");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Record_value_equality_uses_only_the_data_properties()
    {
        await this.SwitchToMainThread();
        var a = new PersonRecord { Name = "Ada", Age = 36 };
        var b = new PersonRecord { Name = "Ada", Age = 36 };

        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());

        b.Age = 37;
        (a == b).Should().BeFalse();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Record_ToString_hides_the_generated_signals()
    {
        await this.SwitchToMainThread();
        var person = new PersonRecord { Name = "Ada", Age = 36 };

        person.ToString().Should().Be("PersonRecord { Name = Ada, Age = 36 }");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Record_with_expression_copies_the_data_properties()
    {
        await this.SwitchToMainThread();
        var person = new PersonRecord { Name = "Ada", Age = 36 };

        var older = person with { Age = 40 };

        older.Name.Should().Be("Ada");
        older.Age.Should().Be(40);
        person.Age.Should().Be(36);
        older.ToString().Should().Be("PersonRecord { Name = Ada, Age = 40 }");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Record_struct_equality_and_ToString_use_the_data_properties()
    {
        await this.SwitchToMainThread();
        var a = new PointRecordStruct { X = 3 };
        var b = new PointRecordStruct { X = 3 };

        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.ToString().Should().Be("PointRecordStruct { X = 3 }");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Effect_reacts_to_generated_property_changes()
    {
        await this.SwitchToMainThread();
        var person = new Person { Name = "Ada" };
        var seen = new List<string>();

        using var effect = new Effect(() => seen.Add(person.Name));

        await TestHelpers.WaitUntil(() => seen.Contains("Ada"));

        person.Name = "Grace";

        await TestHelpers.WaitUntil(() => seen.Contains("Grace"));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task EffectAttribute_runs_once_at_construction()
    {
        await this.SwitchToMainThread();
        var model = new WithEffect { Value = 5 };

        await TestHelpers.WaitUntil(() => model.LastSeen == 5);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task EffectAttribute_reruns_when_a_dependency_changes()
    {
        await this.SwitchToMainThread();
        var model = new WithEffect { Value = 1 };

        await TestHelpers.WaitUntil(() => model.LastSeen == 1);

        model.Value = 2;

        await TestHelpers.WaitUntil(() => model.LastSeen == 2);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task EffectAttribute_does_not_generate_a_public_member()
    {
        await this.SwitchToMainThread();

        typeof(WithEffect).GetProperty("TrackValue").Should().BeNull();
        typeof(WithEffect).GetProperty("TrackValueEffect").Should().BeNull();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task AsyncEffectAttribute_reruns_when_a_dependency_changes()
    {
        await this.SwitchToMainThread();
        var model = new WithAsyncEffect { Value = 1 };

        await TestHelpers.WaitUntil(() => model.LastSeen == 1);

        model.Value = 2;

        await TestHelpers.WaitUntil(() => model.LastSeen == 2);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task AsyncEffectAttribute_supports_Task_returning_methods()
    {
        await this.SwitchToMainThread();
        var model = new WithAsyncEffectTask { Value = 7 };

        await TestHelpers.WaitUntil(() => model.LastSeen == 7);
    }
}
