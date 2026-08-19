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

        [SignalQueryable]
        public string Format(string separator) => $"{City}{separator}{Zip}";

        public string NotExposed() => "no";
    }

    [SignalQueryable]
    sealed class Region
    {
        public string Name { get; set; } = "EU";

        public string Upper() => Name.ToUpperInvariant();

        public string Repeat(int times) => string.Concat(Enumerable.Repeat(Name, times));
    }

    sealed class Employee
    {
        public string Name { get; set; } = "Ada";
        public int Age { get; set; } = 36;
        public Address? Home { get; set; } = new();
        public List<Address> Sites { get; set; } = [new(), new() { City = "Paris", Zip = "75" }];

        public Region Region { get; set; } = new();

        [JsonPropertyName("employee_id")]
        public int Id { get; set; } = 7;

        [JsonIgnore]
        public string Secret { get; set; } = "hidden";

        [SignalQueryable]
        public string Greet() => $"Hello {Name}";

        [SignalQueryable]
        public Address SiteAt(int index) => Sites[index];

        [SignalQueryable]
        public string Describe(string prefix, bool upper = false) =>
            upper ? $"{prefix}{Name}".ToUpperInvariant() : $"{prefix}{Name}";

        [SignalQueryable]
        public List<Address> SitesIn(string city) => Sites.Where(x => x.City == city).ToList();

        [SignalQueryable]
        public double Scaled(double factor) => Age * factor;

        [SignalQueryable]
        public int Overloaded(int a) => a;

        [SignalQueryable]
        public int Overloaded(string b) => b.Length;

        public void Nothing()
        {
        }

        public Task<int> Async() => Task.FromResult(1);
    }

    static readonly JsonSerializerOptions PascalCase = new();

    static Func<Employee, object?> Compile(SignalComputedQuery query, JsonSerializerOptions? options = null) =>
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
    public void MethodWithoutArguments_IsCallable()
    {
        var f = Compile("{ greet }");

        Json(f(new Employee())).ShouldBe("""{"greet":"Hello Ada"}""");
    }

    [Fact]
    public void MethodWithEmptyArgumentSyntax_IsNotValid()
    {
        Should.Throw<FormatException>(() => Compile("{ greet() }"));
    }

    [Fact]
    public void MethodWithArgument_IsCalledWithIt()
    {
        var f = Compile("{ siteAt(index: 1) { city } }");

        Json(f(new Employee())).ShouldBe("""{"siteAt":{"city":"Paris"}}""");
    }

    [Fact]
    public void MethodResultWithoutSelectionSet_ReturnsTheWholeValue()
    {
        var f = Compile("{ siteAt(index: 0) }");

        Json(f(new Employee())).ShouldBe("""{"siteAt":{"city":"London","zip":"E1"}}""");
    }

    [Fact]
    public void MethodReturningACollection_MapsSelectionOverEachElement()
    {
        var f = Compile("""{ sitesIn(city: "Paris") { zip } }""");

        Json(f(new Employee())).ShouldBe("""{"sitesIn":[{"zip":"75"}]}""");
    }

    [Fact]
    public void OptionalArgument_FallsBackToItsDefault()
    {
        var f = Compile("""{ describe(prefix: "Dr ") }""");

        Json(f(new Employee())).ShouldBe("""{"describe":"Dr Ada"}""");
    }

    [Fact]
    public void OptionalArgument_IsUsedWhenSupplied()
    {
        var f = Compile("""{ describe(prefix: "Dr ", upper: true) }""");

        Json(f(new Employee())).ShouldBe("""{"describe":"DR ADA"}""");
    }

    [Fact]
    public void IntegerLiteral_ConvertsToADoubleParameter()
    {
        var f = Compile("{ scaled(factor: 2) }");

        Json(f(new Employee())).ShouldBe("""{"scaled":72}""");
    }

    [Fact]
    public void Alias_RenamesTheProjectedKey()
    {
        var f = Compile("{ first: siteAt(index: 0) { city } }");

        Json(f(new Employee())).ShouldBe("""{"first":{"city":"London"}}""");
    }

    [Fact]
    public void Aliases_DisambiguateTwoCallsToTheSameMethod()
    {
        var f = Compile("{ first: siteAt(index: 0) { city } second: siteAt(index: 1) { city } }");

        Json(f(new Employee())).ShouldBe("""{"first":{"city":"London"},"second":{"city":"Paris"}}""");
    }

    [Fact]
    public void Alias_AppliesToPlainPropertiesToo()
    {
        var f = Compile("{ who: name }");

        Json(f(new Employee())).ShouldBe("""{"who":"Ada"}""");
    }

    [Fact]
    public void MissingRequiredArgument_ThrowsAtCompileTime()
    {
        Should.Throw<FormatException>(() => Compile("{ siteAt }"));
    }

    [Fact]
    public void UnknownArgumentName_ThrowsAtCompileTime()
    {
        Should.Throw<FormatException>(() => Compile("{ siteAt(nope: 1) }"))
              .Message.ShouldContain("nope");
    }

    [Fact]
    public void ArgumentOfTheWrongType_ThrowsAtCompileTime()
    {
        Should.Throw<FormatException>(() => Compile("""{ siteAt(index: "x") }"""));
    }

    [Fact]
    public void NullArgumentForAValueTypeParameter_ThrowsAtCompileTime()
    {
        Should.Throw<FormatException>(() => Compile("{ siteAt(index: null) }"));
    }

    [Fact]
    public void UnknownMethod_ThrowsAtCompileTime()
    {
        Should.Throw<FormatException>(() => Compile("{ nope(a: 1) }"));
    }

    [Fact]
    public void AmbiguousOverload_ThrowsAtCompileTime()
    {
        Should.Throw<FormatException>(() => Compile("{ overloaded }"));
    }

    [Fact]
    public void Overload_IsSelectedByArgumentName()
    {
        var f = Compile("""{ byInt: overloaded(a: 5) byString: overloaded(b: "abc") }""");

        Json(f(new Employee())).ShouldBe("""{"byInt":5,"byString":3}""");
    }

    [Fact]
    public void MethodWithoutTheAttribute_IsNotCallable()
    {
        var error = Should.Throw<FormatException>(() => Compile("{ home { notExposed } }"));

        error.Message.ShouldContain("SignalQueryable");
    }

    [Fact]
    public void MethodOnANestedObject_IsCallable()
    {
        var f = Compile("""{ home { format(separator: " - ") } }""");

        Json(f(new Employee())).ShouldBe("""{"home":{"format":"London - E1"}}""");
    }

    [Fact]
    public void MethodOnACollectionElement_IsCalledPerElement()
    {
        var f = Compile("""{ sites { format(separator: "/") } }""");

        Json(f(new Employee())).ShouldBe("""{"sites":[{"format":"London/E1"},{"format":"Paris/75"}]}""");
    }

    [Fact]
    public void MethodOnAMethodResult_IsCallable()
    {
        var f = Compile("""{ siteAt(index: 1) { format(separator: ", ") } }""");

        Json(f(new Employee())).ShouldBe("""{"siteAt":{"format":"Paris, 75"}}""");
    }

    [Fact]
    public void AttributeOnTheClass_ExposesEveryPublicMethod()
    {
        var f = Compile("{ region { upper repeat(times: 2) } }");

        Json(f(new Employee())).ShouldBe("""{"region":{"upper":"EU","repeat":"EUEU"}}""");
    }

    [Fact]
    public void VoidMethod_IsNotCallable()
    {
        Should.Throw<FormatException>(() => Compile("{ nothing }"));
    }

    [Fact]
    public void AsyncMethod_IsNotCallable()
    {
        Should.Throw<FormatException>(() => Compile("{ async }"));
    }

    [Fact]
    public void PascalCaseOptions_UseTheClrMethodName()
    {
        var f = Compile("{ SiteAt(index: 0) { City } }", PascalCase);

        JsonSerializer.Serialize(f(new Employee()), PascalCase).ShouldBe("""{"SiteAt":{"City":"London"}}""");
    }

    [Fact]
    public void MalformedQuery_Throws()
    {
        Should.Throw<FormatException>(() => Compile("{ name"));
    }
}
