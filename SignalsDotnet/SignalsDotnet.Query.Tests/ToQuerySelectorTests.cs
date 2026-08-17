using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;

namespace SignalsDotnet.Query.Tests;

public class ToQuerySelectorTests
{
    sealed class Address
    {
        public string City { get; set; } = "London";
        public string Zip { get; set; } = "E1";
    }

    sealed class Employee
    {
        public string Name { get; set; } = "Ada";
        public int Age { get; set; } = 36;
        public Address? Home { get; set; } = new();
        public List<Address> Sites { get; set; } = [new(), new() { City = "Paris", Zip = "75" }];

        [JsonPropertyName("employee_id")]
        public int Id { get; set; } = 7;

        [JsonIgnore]
        public string Secret { get; set; } = "hidden";
    }

    static readonly JsonSerializerOptions PascalCase = new();

    static Func<Employee, object?> Compile(SignalsQuery query, JsonSerializerOptions? options = null) =>
        query.ToQuerySelector<Employee>(options);

    static string Json(object? value) => JsonSerializer.Serialize(value, SignalsQueryExtensions.DefaultJsonOptions);

    [Fact]
    public void SelectedProperty_IsProjected()
    {
        var f = Compile("{ name }");

        Json(f(new Employee())).ShouldBe("""{"name":"Ada"}""");
    }

    [Fact]
    public void UnselectedProperties_AreOmitted()
    {
        var f = Compile("{ age }");

        Json(f(new Employee())).ShouldBe("""{"age":36}""");
    }

    [Fact]
    public void ProjectionOrder_FollowsTheQuery()
    {
        var f = Compile("{ age name }");

        Json(f(new Employee())).ShouldBe("""{"age":36,"name":"Ada"}""");
    }

    [Fact]
    public void NestedSelection_ProjectsSubObject()
    {
        var f = Compile("{ home { city } }");

        Json(f(new Employee())).ShouldBe("""{"home":{"city":"London"}}""");
    }

    [Fact]
    public void PropertyWithoutSelectionSet_ReturnsWholeValue()
    {
        var f = Compile("{ home }");

        Json(f(new Employee())).ShouldBe("""{"home":{"city":"London","zip":"E1"}}""");
    }

    [Fact]
    public void Collections_MapSelectionOverEachElement()
    {
        var f = Compile("{ sites { city } }");

        Json(f(new Employee())).ShouldBe("""{"sites":[{"city":"London"},{"city":"Paris"}]}""");
    }

    [Fact]
    public void NullReference_ProjectsToNull()
    {
        var f = Compile("{ home { city } }");

        Json(f(new Employee { Home = null })).ShouldBe("""{"home":null}""");
    }

    [Fact]
    public void NullCollection_ProjectsToNull()
    {
        var f = Compile("{ sites { city } }");

        Json(f(new Employee { Sites = null! })).ShouldBe("""{"sites":null}""");
    }

    [Fact]
    public void JsonPropertyNameAttribute_IsTheQueryName()
    {
        var f = Compile("{ employee_id }");

        Json(f(new Employee())).ShouldBe("""{"employee_id":7}""");
    }

    [Fact]
    public void ClrNameOfRenamedProperty_IsNotAccepted()
    {
        Should.Throw<FormatException>(() => Compile("{ Id }"));
    }

    [Fact]
    public void TheWebDefault_MakesCamelCaseTheQueryName()
    {
        var f = Compile("{ name }");

        Json(f(new Employee())).ShouldBe("""{"name":"Ada"}""");
    }

    [Fact]
    public void TheWebDefault_RejectsThePascalName()
    {
        Should.Throw<FormatException>(() => Compile("{ Name }"));
    }

    [Fact]
    public void ExplicitOptions_OverrideTheWebDefault()
    {
        var f = Compile("{ Name }", PascalCase);

        JsonSerializer.Serialize(f(new Employee()), PascalCase).ShouldBe("""{"Name":"Ada"}""");
    }

    [Fact]
    public void ExplicitPascalCaseOptions_RejectTheCamelName()
    {
        Should.Throw<FormatException>(() => Compile("{ name }", PascalCase));
    }

    [Fact]
    public void IgnoredProperty_IsNotSelectable()
    {
        Should.Throw<FormatException>(() => Compile("{ secret }"));
    }

    [Fact]
    public void UnknownProperty_ThrowsAtCompileTime()
    {
        Should.Throw<FormatException>(() => Compile("{ nope }"));
    }

    [Fact]
    public void UnknownNestedProperty_ThrowsAtCompileTime()
    {
        Should.Throw<FormatException>(() => Compile("{ home { nope } }"));
    }

    [Fact]
    public void CompiledDelegate_IsReusableAcrossInstances()
    {
        var f = Compile("{ name }");

        Json(f(new Employee { Name = "Ada" })).ShouldBe("""{"name":"Ada"}""");
        Json(f(new Employee { Name = "Bob" })).ShouldBe("""{"name":"Bob"}""");
    }

    [Fact]
    public void StringProperty_IsNotTreatedAsACollection()
    {
        var f = Compile("{ name }");

        Json(f(new Employee())).ShouldBe("""{"name":"Ada"}""");
    }

    [Fact]
    public void MalformedQuery_Throws()
    {
        Should.Throw<FormatException>(() => Compile("{ name"));
    }
}
