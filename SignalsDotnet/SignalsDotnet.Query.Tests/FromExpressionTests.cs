using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;

namespace SignalsDotnet.Query.Tests;

public class FromExpressionTests
{
    public sealed class Address
    {
        public string City { get; set; } = "London";
        public string Zip { get; set; } = "E1";

        [SignalQueryable]
        public string Format(string separator) => $"{City}{separator}{Zip}";

        public string NotExposed() => "no";
    }

    [SignalQueryable]
    public sealed class Region
    {
        public string Name { get; set; } = "EU";

        public string Upper() => Name.ToUpperInvariant();

        public string Repeat(int times) => string.Concat(Enumerable.Repeat(Name, times));
    }

    public enum Level
    {
        Low,
        High
    }

    public sealed class Employee
    {
        public string Name { get; set; } = "Ada";
        public int Age { get; set; } = 36;
        public Address? Home { get; set; } = new();
        public List<Address> Sites { get; set; } = [new(), new() { City = "Paris", Zip = "75" }];

        public Region Region { get; set; } = new();

        public Dictionary<string, Address> Offices { get; set; } = new() { ["hq"] = new() };

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
        public string AtLevel(Level level) => $"{Name}:{level}";

        [SignalQueryable]
        public Task<string> GreetAsync(string prefix) => Task.FromResult($"{prefix}{Name}");

        [SignalQueryable]
        public ValueTask<Address?> HomeAsync() => new(Home);

        [SignalQueryable]
        public Task<List<Address>> SitesAsync() => Task.FromResult(Sites);
    }

    static readonly NamingConventionOptions PascalCase = NamingConventionOptions.FromJson(new());

    static string Text(Expression<Func<Employee, object?>> selector, NamingConventionOptions? options = null) =>
        SignalComputedQuery.Create(selector, options).Text;

    static string Json(object? value) => JsonSerializer.Serialize(value, SignalsQueryExtensions.DefaultJsonOptions);

    [Fact]
    public void FromExpression_ReturnsATypedQuery()
    {
        Expression<Func<Employee, object?>> selector = x => new { x.Name };

        SignalComputedQuery.Create(selector).ShouldBeOfType<SignalComputedQuery<Employee, object?>>();
    }

    [Fact]
    public void TypedQuery_KeepsTheSelector()
    {
        Expression<Func<Employee, object?>> selector = x => new { x.Name };

        SignalComputedQuery.Create(selector).Selector.ShouldBeSameAs(selector);
    }

    [Fact]
    public void TypedQuery_EqualsTheParsedEquivalent()
    {
        SignalComputedQuery.Create((Employee x) => new { x.Name })
                           .ShouldBe(SignalComputedQuery.Parse("{ name }"));
    }

    [Fact]
    public void SingleProperty_BecomesASelectionSet()
    {
        Text(x => x.Name).ShouldBe("{ name }");
    }

    [Fact]
    public void AnonymousType_BecomesOneFieldPerMember()
    {
        Text(x => new { x.Name, x.Age }).ShouldBe("{ name age }");
    }

    [Fact]
    public void MemberOrder_IsPreserved()
    {
        Text(x => new { x.Age, x.Name }).ShouldBe("{ age name }");
    }

    [Fact]
    public void NestedMemberAccess_BecomesANestedSelection()
    {
        Text(x => x.Home!.City).ShouldBe("{ home { city } }");
    }

    [Fact]
    public void RenamedMember_BecomesAnAlias()
    {
        Text(x => new { Town = x.Home!.City }).ShouldBe("{ town: home { city } }");
    }

    [Fact]
    public void MemberNamedLikeTheField_HasNoAlias()
    {
        Text(x => new { Name = x.Name }).ShouldBe("{ name }");
    }

    [Fact]
    public void JsonPropertyNameAttribute_IsUsedAsTheFieldName()
    {
        Text(x => x.Id).ShouldBe("{ employee_id }");
    }

    [Fact]
    public void IgnoredProperty_IsNotTranslatable()
    {
        Should.Throw<NotSupportedException>(() => Text(x => x.Secret));
    }

    [Fact]
    public void TheWebDefault_MakesFieldNamesCamelCase()
    {
        Text(x => new { x.Name, x.Age }).ShouldBe("{ name age }");
    }

    [Fact]
    public void ExplicitOptions_OverrideTheWebDefault()
    {
        Text(x => new { x.Name }, PascalCase).ShouldBe("{ Name }");
    }

    [Fact]
    public void QueryableMethod_BecomesACallField()
    {
        Text(x => x.Greet()).ShouldBe("{ greet }");
    }

    [Fact]
    public void MethodArguments_AreWrittenAsNamedArguments()
    {
        Text(x => x.SiteAt(1).City).ShouldBe("{ siteAt(index: 1) { city } }");
    }

    [Fact]
    public void StringArguments_AreQuoted()
    {
        Text(x => x.SitesIn("Paris")).ShouldBe("""{ sitesIn(city: "Paris") }""");
    }

    [Fact]
    public void OptionalArguments_AreWrittenExplicitly()
    {
        Text(x => x.Describe("Mrs ")).ShouldBe("""{ describe(prefix: "Mrs ", upper: false) }""");
    }

    [Fact]
    public void CapturedVariables_AreEvaluatedIntoLiterals()
    {
        var index = 1;

        Text(x => x.SiteAt(index).Zip).ShouldBe("{ siteAt(index: 1) { zip } }");
    }

    [Fact]
    public void DoubleArguments_KeepAnInvariantDecimalPoint()
    {
        Text(x => x.Scaled(1.5)).ShouldBe("{ scaled(factor: 1.5) }");
    }

    [Fact]
    public void WholeDoubleArguments_StayFloating()
    {
        Text(x => x.Scaled(2)).ShouldBe("{ scaled(factor: 2.0) }");
    }

    [Fact]
    public void EnumArguments_AreWrittenByName()
    {
        Text(x => x.AtLevel(Level.High)).ShouldBe("""{ atLevel(level: "High") }""");
    }

    [Fact]
    public void QuotesAndBackslashes_AreEscaped()
    {
        Text(x => x.SitesIn("""a"b\c""")).ShouldBe("""{ sitesIn(city: "a\"b\\c") }""");
    }

    [Fact]
    public void NewlinesAndTabs_AreEscaped()
    {
        Text(x => x.SitesIn("a\nb\tc")).ShouldBe("""{ sitesIn(city: "a\nb\tc") }""");
    }

    [Fact]
    public void UnannotatedMethod_IsNotTranslatable()
    {
        Should.Throw<NotSupportedException>(() => Text(x => x.Home!.NotExposed()));
    }

    [Fact]
    public void MethodOnAQueryableType_NeedsNoAnnotation()
    {
        Text(x => x.Region.Upper()).ShouldBe("{ region { upper } }");
    }

    [Fact]
    public void AsyncMethod_IsTranslatedLikeAnyOtherCall()
    {
        Text(x => x.GreetAsync("Hi ")).ShouldBe("""{ greetAsync(prefix: "Hi ") }""");
    }

    [Fact]
    public void ResultOnATask_IsNotTranslatable()
    {
        Should.Throw<NotSupportedException>(() => Text(x => x.HomeAsync().Result!.City));
    }

    [Fact]
    public void AwaitOnAnAsyncMethod_MeansAwait()
    {
        Text(x => x.HomeAsync().Await()!.City).ShouldBe("{ homeAsync { city } }");
    }

    [Fact]
    public void AwaitOnATask_MeansAwait()
    {
        Text(x => x.GreetAsync("Hi ").Await()).ShouldBe("""{ greetAsync(prefix: "Hi ") }""");
    }

    [Fact]
    public void AwaitedCollectionWithAwait_MapsSelectionOverEachElement()
    {
        Text(x => x.SitesAsync().Await().Select(s => s.City).ToList()).ShouldBe("{ sitesAsync { city } }");
    }

    [Fact]
    public void AwaitOutsideAQuery_Throws()
    {
        Should.Throw<InvalidOperationException>(() => Task.FromResult(1).Await());
    }

    [Fact]
    public void ToListOnAProjection_IsTransparent()
    {
        Text(x => x.Sites.Select(s => s.City).ToList()).ShouldBe("{ sites { city } }");
    }

    [Fact]
    public void ToArrayOnAProjection_IsTransparent()
    {
        Text(x => x.Sites.Select(s => s.City).ToArray()).ShouldBe("{ sites { city } }");
    }

    [Fact]
    public void SelectOverACollection_ProjectsEachElement()
    {
        Text(x => x.Sites.Select(s => s.City)).ShouldBe("{ sites { city } }");
    }

    [Fact]
    public void SelectWithAnAnonymousType_ProjectsEveryMember()
    {
        Text(x => x.Sites.Select(s => new { s.City, s.Zip })).ShouldBe("{ sites { city zip } }");
    }

    [Fact]
    public void SelectOverADictionary_ProjectsTheValues()
    {
        Text(x => x.Offices.Select(o => o.Value.City)).ShouldBe("{ offices { city } }");
    }

    [Fact]
    public void CollectionWithoutSelect_SelectsTheWholeValue()
    {
        Text(x => x.Sites).ShouldBe("{ sites }");
    }

    [Fact]
    public void MemberOfACollectionElement_ResolvesAgainstTheElementType()
    {
        Text(x => new { Cities = x.Sites.Select(s => s.City) }).ShouldBe("{ cities: sites { city } }");
    }

    [Fact]
    public void ArbitraryComputation_IsNotTranslatable()
    {
        Should.Throw<NotSupportedException>(() => Text(x => x.Age + 1));
    }

    [Fact]
    public void ConstantSelection_IsNotTranslatable()
    {
        Should.Throw<NotSupportedException>(() => Text(_ => 42));
    }

    [Fact]
    public void StringInterpolation_IsNotTranslatable()
    {
        Should.Throw<NotSupportedException>(() => Text(x => $"{x.Name}!"));
    }

    [Fact]
    public void UnsupportedOperator_IsNotTranslatable()
    {
        Should.Throw<NotSupportedException>(() => Text(x => x.Sites.Where(s => s.City == "Paris")));
    }

    [Fact]
    public void NullSelector_Throws()
    {
        Should.Throw<ArgumentNullException>(() => SignalComputedQuery.Create<Employee>(null!));
    }

    [Theory]
    [MemberData(nameof(Selectors))]
    public void TranslatedText_ParsesBackIntoTheSameQuery(Expression<Func<Employee, object?>> selector)
    {
        var query = SignalComputedQuery.Create(selector);

        SignalComputedQuery.Parse(query.Text).ShouldBe(query);
    }

    [Theory]
    [MemberData(nameof(Selectors))]
    public void TranslatedText_ProjectsTheSameShapeItSelected(Expression<Func<Employee, object?>> selector)
    {
        var query = SignalComputedQuery.Create(selector);

        Should.NotThrow(() => query.IsAsync<Employee>()
            ? query.ToAsyncQuerySelector<Employee>()
            : (object)query.ToQuerySelector<Employee>());
    }

    [Fact]
    public void RoundTrippedQuery_ProducesTheExpectedProjection()
    {
        var query = SignalComputedQuery.Create((Employee x) => new
        {
            x.Name,
            Town = x.Home!.City,
            Greeting = x.Greet(),
            Cities = x.Sites.Select(s => s.City)
        });

        var projection = query.ToQuerySelector<Employee>()(new Employee());

        Json(projection).ShouldBe("""{"name":"Ada","town":{"city":"London"},"greeting":"Hello Ada","cities":[{"city":"London"},{"city":"Paris"}]}""");
    }

    [Fact]
    public void EquivalentExpressionAndText_ProduceEqualQueries()
    {
        SignalComputedQuery.Create((Employee x) => new { x.Name, Town = x.Home!.City })
                           .ShouldBe(SignalComputedQuery.Parse("{ name town: home { city } }"));
    }

    public static TheoryData<Expression<Func<Employee, object?>>> Selectors() =>
    [
        x => x.Name,
        x => new { x.Name, x.Age },
        x => x.Home!.City,
        x => new { Town = x.Home!.City },
        x => x.Id,
        x => x.Greet(),
        x => x.SiteAt(1).City,
        x => x.SitesIn("Paris"),
        x => x.Describe("Mrs "),
        x => x.Scaled(1.5),
        x => x.Region.Upper(),
        x => x.Sites.Select(s => new { s.City, s.Zip }),
        x => x.Offices.Select(o => o.Value.City),
        x => new { x.Name, Cities = x.Sites.Select(s => s.City) },
    ];
}
