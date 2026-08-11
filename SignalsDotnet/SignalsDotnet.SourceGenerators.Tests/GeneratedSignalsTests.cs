using System.ComponentModel;
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
    public async Task OnInitialized_runs_after_the_signals_are_ready()
    {
        await this.SwitchToMainThread();
        var model = new WithInitializationHook();

        model.Name.Should().Be("from hook");
        model.ShoutAtInitialization.Should().Be("FROM HOOK");
        model.Shout.Should().Be("FROM HOOK");
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
}
