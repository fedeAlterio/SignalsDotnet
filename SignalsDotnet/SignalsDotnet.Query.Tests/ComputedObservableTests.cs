using System.Collections.ObjectModel;
using System.Text.Json;
using R3;
using Shouldly;
using SignalsDotnet;
using SignalsDotnet.Query;

namespace SignalsDotnet.Query.Tests;

[GenerateSignals]
public partial class Address
{
    public partial string City { get; set; }
    public partial string Zip { get; set; }

    [SignalQueryable]
    public string Format(string separator) => $"{City}{separator}{Zip}";
}

[GenerateSignals]
public partial class Employee
{
    public partial string Name { get; set; }
    public partial int Age { get; set; }
    public partial Address? Home { get; set; }

    [SignalQueryable]
    public string Greet(string prefix) => $"{prefix}{Name}";

    [SignalQueryable]
    public int AgeIn(int years) => Age + years;
}

[GenerateSignals]
public partial class Team
{
    public partial string Label { get; set; }
    [SignalIgnore]
    public CollectionSignal<ObservableCollection<Employee>> Members { get; } = new();

    [SignalIgnore]
    public DictionarySignal<string, Employee> ByRole { get; } = new();
}

public class ComputedObservableTests
{
    sealed class Recorder : IDisposable
    {
        readonly List<string> _emissions = [];
        readonly IDisposable _subscription;

        public Recorder(Observable<object?> observable) =>
            _subscription = observable.Subscribe(x => _emissions.Add(JsonSerializer.Serialize(x)));

        public IReadOnlyList<string> Emissions => _emissions;
        public int Count => _emissions.Count;
        public string Last => _emissions[^1];
        public void Dispose() => _subscription.Dispose();
    }

    static Employee NewEmployee(string name = "Ada", int age = 36) =>
        new() { Name = name, Age = age, Home = new Address { City = "London", Zip = "E1" } };

    [Fact]
    public void Subscribing_EmitsTheInitialProjection()
    {
        using var recorder = new Recorder(new SignalsQuery("{ name }").ComputedObservable(NewEmployee()));

        recorder.Count.ShouldBe(1);
        recorder.Last.ShouldBe("""{"name":"Ada"}""");
    }

    [Fact]
    public void ChangingASelectedProperty_Emits()
    {
        var employee = NewEmployee();
        using var recorder = new Recorder(new SignalsQuery("{ name }").ComputedObservable(employee));

        employee.Name = "Bob";

        recorder.Count.ShouldBe(2);
        recorder.Last.ShouldBe("""{"name":"Bob"}""");
    }

    [Fact]
    public void ChangingAnUnselectedProperty_DoesNotEmit()
    {
        var employee = NewEmployee();
        using var recorder = new Recorder(new SignalsQuery("{ name }").ComputedObservable(employee));

        employee.Age = 99;

        recorder.Count.ShouldBe(1);
    }

    [Fact]
    public void EachSelectedProperty_TriggersItsOwnEmission()
    {
        var employee = NewEmployee();
        using var recorder = new Recorder(new SignalsQuery("{ name age }").ComputedObservable(employee));

        employee.Name = "Bob";
        employee.Age = 40;

        recorder.Emissions.ShouldBe([
            """{"name":"Ada","age":36}""",
            """{"name":"Bob","age":36}""",
            """{"name":"Bob","age":40}"""
        ]);
    }

    [Fact]
    public void ChangingANestedSelectedProperty_Emits()
    {
        var employee = NewEmployee();
        using var recorder = new Recorder(new SignalsQuery("{ home { city } }").ComputedObservable(employee));

        employee.Home!.City = "Paris";

        recorder.Count.ShouldBe(2);
        recorder.Last.ShouldBe("""{"home":{"city":"Paris"}}""");
    }

    [Fact]
    public void ChangingAnUnselectedNestedProperty_DoesNotEmit()
    {
        var employee = NewEmployee();
        using var recorder = new Recorder(new SignalsQuery("{ home { city } }").ComputedObservable(employee));

        employee.Home!.Zip = "75001";

        recorder.Count.ShouldBe(1);
    }

    [Fact]
    public void ReplacingANestedModel_Emits()
    {
        var employee = NewEmployee();
        using var recorder = new Recorder(new SignalsQuery("{ home { city } }").ComputedObservable(employee));

        employee.Home = new Address { City = "Rome", Zip = "00100" };

        recorder.Count.ShouldBe(2);
        recorder.Last.ShouldBe("""{"home":{"city":"Rome"}}""");
    }

    [Fact]
    public void ChangingAPropertyOnAReplacedNestedModel_StillEmits()
    {
        var employee = NewEmployee();
        using var recorder = new Recorder(new SignalsQuery("{ home { city } }").ComputedObservable(employee));

        employee.Home = new Address { City = "Rome", Zip = "00100" };
        employee.Home.City = "Milan";

        recorder.Count.ShouldBe(3);
        recorder.Last.ShouldBe("""{"home":{"city":"Milan"}}""");
    }

    [Fact]
    public void NullNestedModel_ProjectsToNullAndEmitsWhenSet()
    {
        var employee = NewEmployee();
        employee.Home = null;

        using var recorder = new Recorder(new SignalsQuery("{ home { city } }").ComputedObservable(employee));
        recorder.Last.ShouldBe("""{"home":null}""");

        employee.Home = new Address { City = "Oslo", Zip = "0150" };

        recorder.Count.ShouldBe(2);
        recorder.Last.ShouldBe("""{"home":{"city":"Oslo"}}""");
    }

    [Fact]
    public void CollectionSignal_ProjectsElements()
    {
        var team = new Team { Label = "Core" };
        team.Members.Value = [NewEmployee("Ada"), NewEmployee("Bob")];

        using var recorder = new Recorder(new SignalsQuery("{ members { name } }").ComputedObservable(team));

        recorder.Last.ShouldBe("""{"members":[{"name":"Ada"},{"name":"Bob"}]}""");
    }

    [Fact]
    public void AddingToACollectionSignal_Emits()
    {
        var team = new Team { Label = "Core" };
        team.Members.Value = [NewEmployee("Ada")];

        using var recorder = new Recorder(new SignalsQuery("{ members { name } }").ComputedObservable(team));

        team.Members.Value!.Add(NewEmployee("Bob"));

        recorder.Count.ShouldBe(2);
        recorder.Last.ShouldBe("""{"members":[{"name":"Ada"},{"name":"Bob"}]}""");
    }

    [Fact]
    public void RemovingFromACollectionSignal_Emits()
    {
        var team = new Team { Label = "Core" };
        team.Members.Value = [NewEmployee("Ada"), NewEmployee("Bob")];

        using var recorder = new Recorder(new SignalsQuery("{ members { name } }").ComputedObservable(team));

        team.Members.Value!.RemoveAt(0);

        recorder.Count.ShouldBe(2);
        recorder.Last.ShouldBe("""{"members":[{"name":"Bob"}]}""");
    }

    [Fact]
    public void ChangingAPropertyOfACollectionElement_Emits()
    {
        var team = new Team { Label = "Core" };
        team.Members.Value = [NewEmployee("Ada")];

        using var recorder = new Recorder(new SignalsQuery("{ members { name } }").ComputedObservable(team));

        team.Members.Value![0].Name = "Grace";

        recorder.Count.ShouldBe(2);
        recorder.Last.ShouldBe("""{"members":[{"name":"Grace"}]}""");
    }

    [Fact]
    public void ChangingAnUnselectedPropertyOfACollectionElement_DoesNotEmit()
    {
        var team = new Team { Label = "Core" };
        team.Members.Value = [NewEmployee("Ada")];

        using var recorder = new Recorder(new SignalsQuery("{ members { name } }").ComputedObservable(team));

        team.Members.Value![0].Age = 77;

        recorder.Count.ShouldBe(1);
    }

    [Fact]
    public void ReplacingTheWholeCollection_Emits()
    {
        var team = new Team { Label = "Core" };
        team.Members.Value = [NewEmployee("Ada")];

        using var recorder = new Recorder(new SignalsQuery("{ members { name } }").ComputedObservable(team));

        team.Members.Value = [NewEmployee("Cy")];

        recorder.Count.ShouldBe(2);
        recorder.Last.ShouldBe("""{"members":[{"name":"Cy"}]}""");
    }

    [Fact]
    public void DictionarySignal_ProjectsEntries()
    {
        var team = new Team { Label = "Core" };
        team.ByRole.Add("lead", NewEmployee("Ada"));

        using var recorder = new Recorder(new SignalsQuery("{ byRole { name } }").ComputedObservable(team));

        recorder.Last.ShouldBe("""{"byRole":{"lead":{"name":"Ada"}}}""");
    }

    [Fact]
    public void AddingToADictionarySignal_Emits()
    {
        var team = new Team { Label = "Core" };
        team.ByRole.Add("lead", NewEmployee("Ada"));

        using var recorder = new Recorder(new SignalsQuery("{ byRole { name } }").ComputedObservable(team));

        team.ByRole.Add("dev", NewEmployee("Bob"));

        recorder.Count.ShouldBe(2);
        recorder.Last.ShouldBe("""{"byRole":{"lead":{"name":"Ada"},"dev":{"name":"Bob"}}}""");
    }

    [Fact]
    public void RemovingFromADictionarySignal_Emits()
    {
        var team = new Team { Label = "Core" };
        team.ByRole.Add("lead", NewEmployee("Ada"));
        team.ByRole.Add("dev", NewEmployee("Bob"));

        using var recorder = new Recorder(new SignalsQuery("{ byRole { name } }").ComputedObservable(team));

        team.ByRole.Remove("lead");

        recorder.Count.ShouldBe(2);
        recorder.Last.ShouldBe("""{"byRole":{"dev":{"name":"Bob"}}}""");
    }

    [Fact]
    public void ReplacingADictionaryValue_Emits()
    {
        var team = new Team { Label = "Core" };
        team.ByRole.Add("lead", NewEmployee("Ada"));

        using var recorder = new Recorder(new SignalsQuery("{ byRole { name } }").ComputedObservable(team));

        team.ByRole["lead"] = NewEmployee("Grace");

        recorder.Count.ShouldBe(2);
        recorder.Last.ShouldBe("""{"byRole":{"lead":{"name":"Grace"}}}""");
    }

    [Fact]
    public void ChangingAPropertyOfADictionaryValue_Emits()
    {
        var team = new Team { Label = "Core" };
        team.ByRole.Add("lead", NewEmployee("Ada"));

        using var recorder = new Recorder(new SignalsQuery("{ byRole { name } }").ComputedObservable(team));

        team.ByRole["lead"].Name = "Grace";

        recorder.Count.ShouldBe(2);
        recorder.Last.ShouldBe("""{"byRole":{"lead":{"name":"Grace"}}}""");
    }

    [Fact]
    public void SiblingSignalsAcrossKinds_AllTriggerEmissions()
    {
        var team = new Team { Label = "Core" };
        team.Members.Value = [NewEmployee("Ada")];
        team.ByRole.Add("lead", NewEmployee("Bob"));

        using var recorder = new Recorder(
            new SignalsQuery("{ label members { name } byRole { name } }").ComputedObservable(team));

        team.Label = "Platform";
        team.Members.Value![0].Name = "Grace";
        team.ByRole["lead"].Name = "Cy";

        recorder.Count.ShouldBe(4);
        recorder.Last.ShouldBe("""{"label":"Platform","members":[{"name":"Grace"}],"byRole":{"lead":{"name":"Cy"}}}""");
    }

    [Fact]
    public void UnsubscribingStopsEmissions()
    {
        var employee = NewEmployee();
        var recorder = new Recorder(new SignalsQuery("{ name }").ComputedObservable(employee));

        employee.Name = "Bob";
        recorder.Dispose();
        employee.Name = "Cy";

        recorder.Count.ShouldBe(2);
    }

    [Fact]
    public void TwoSubscribersToTheSameQuery_BothReceiveEmissions()
    {
        var employee = NewEmployee();
        using var first = new Recorder(new SignalsQuery("{ name }").ComputedObservable(employee));
        using var second = new Recorder(new SignalsQuery("{ name }").ComputedObservable(employee));

        employee.Name = "Bob";

        first.Count.ShouldBe(2);
        second.Count.ShouldBe(2);
    }

    [Fact]
    public void AQuery_IsReusableAcrossSources()
    {
        var query = new SignalsQuery("{ name }");

        using var first = new Recorder(query.ComputedObservable(NewEmployee("Ada")));
        using var second = new Recorder(query.ComputedObservable(NewEmployee("Bob")));

        first.Last.ShouldBe("""{"name":"Ada"}""");
        second.Last.ShouldBe("""{"name":"Bob"}""");
    }

    [Fact]
    public void CallingAMethod_EmitsTheInitialResult()
    {
        using var recorder = new Recorder(new SignalsQuery("""{ greet(prefix: "Hi ") }""").ComputedObservable(NewEmployee()));

        recorder.Count.ShouldBe(1);
        recorder.Last.ShouldBe("""{"greet":"Hi Ada"}""");
    }

    [Fact]
    public void ChangingASignalReadByACalledMethod_Emits()
    {
        var employee = NewEmployee();
        using var recorder = new Recorder(new SignalsQuery("""{ greet(prefix: "Hi ") }""").ComputedObservable(employee));

        employee.Name = "Bob";

        recorder.Last.ShouldBe("""{"greet":"Hi Bob"}""");
        recorder.Count.ShouldBe(2);
    }

    [Fact]
    public void ChangingASignalNotReadByACalledMethod_DoesNotEmit()
    {
        var employee = NewEmployee();
        using var recorder = new Recorder(new SignalsQuery("""{ greet(prefix: "Hi ") }""").ComputedObservable(employee));

        employee.Age = 50;

        recorder.Count.ShouldBe(1);
    }

    [Fact]
    public void AliasedCallsToTheSameMethod_TrackTheirOwnArguments()
    {
        var employee = NewEmployee();
        using var recorder = new Recorder(new SignalsQuery("{ now: ageIn(years: 0) later: ageIn(years: 10) }").ComputedObservable(employee));

        recorder.Last.ShouldBe("""{"now":36,"later":46}""");

        employee.Age = 40;

        recorder.Last.ShouldBe("""{"now":40,"later":50}""");
    }
}
