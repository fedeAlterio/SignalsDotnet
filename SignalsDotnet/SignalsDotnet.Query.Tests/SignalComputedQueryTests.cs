using Shouldly;

namespace SignalsDotnet.Query.Tests;

public class SignalComputedQueryTests
{
    [Fact]
    public void Constructor_ParsesTheQuery()
    {
        new SignalComputedQuery("{ Name Age }").Fields.Select(x => x.Name).ShouldBe(["Name", "Age"]);
    }

    [Fact]
    public void Text_PreservesTheOriginalQuery()
    {
        new SignalComputedQuery("{ Name }").Text.ShouldBe("{ Name }");
    }

    [Fact]
    public void MalformedQuery_ThrowsOnConstruction()
    {
        Should.Throw<FormatException>(() => new SignalComputedQuery("{ Name"));
    }

    [Fact]
    public void NullQuery_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new SignalComputedQuery(null!));
    }

    [Fact]
    public void StringLiteral_ConvertsImplicitly()
    {
        SignalComputedQuery query = "{ Name }";

        query.Fields.Count.ShouldBe(1);
    }

    [Fact]
    public void TryParse_ReturnsTheQueryWhenValid()
    {
        SignalComputedQuery.TryParse("{ Name }", out var query).ShouldBeTrue();
        query!.Text.ShouldBe("{ Name }");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ Name")]
    public void TryParse_FailsWithoutThrowing(string? query)
    {
        SignalComputedQuery.TryParse(query, out _).ShouldBeFalse();
    }

    [Fact]
    public void Parse_IsEquivalentToTheConstructor()
    {
        SignalComputedQuery.Parse("{ Name }").ShouldBe(new SignalComputedQuery("{ Name }"));
    }

    [Fact]
    public void Equality_IsStructuralAcrossFormatting()
    {
        new SignalComputedQuery("{ Name, Age }").ShouldBe(new SignalComputedQuery("Name\nAge"));
    }

    [Fact]
    public void Equality_IsStructuralForNestedQueries()
    {
        new SignalComputedQuery("{ Home { City Zip } }").ShouldBe(new SignalComputedQuery("Home{City,Zip}"));
    }

    [Fact]
    public void DifferentNestedSelections_AreNotEqual()
    {
        new SignalComputedQuery("{ Home { City } }").ShouldNotBe(new SignalComputedQuery("{ Home { Zip } }"));
    }

    [Fact]
    public void EqualQueries_ShareAHashCode()
    {
        new SignalComputedQuery("{ Home { City } }").GetHashCode()
            .ShouldBe(new SignalComputedQuery("Home{City}").GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsTheOriginalQuery()
    {
        new SignalComputedQuery("{ Name }").ToString().ShouldBe("{ Name }");
    }

    [Fact]
    public void Equals_HandlesNullAndForeignTypes()
    {
        var query = new SignalComputedQuery("{ Name }");

        query.Equals((SignalComputedQuery?)null).ShouldBeFalse();
        query.Equals((object?)"{ Name }").ShouldBeFalse();
    }

    [Fact]
    public void EqualityOperators_AreStructuralAndNullSafe()
    {
        (new SignalComputedQuery("{ Home { City } }") == new SignalComputedQuery("Home{City}")).ShouldBeTrue();
        (new SignalComputedQuery("{ Name }") != new SignalComputedQuery("{ Age }")).ShouldBeTrue();

        SignalComputedQuery? nothing = null;
        (nothing == null).ShouldBeTrue();
        (new SignalComputedQuery("{ Name }") == null).ShouldBeFalse();
    }

    [Fact]
    public void SameInstance_EqualsItself()
    {
        var query = new SignalComputedQuery("{ Name }");

        query.Equals(query).ShouldBeTrue();
    }

    sealed class Person
    {
        public string Name { get; set; } = "Ada";
    }

    [Fact]
    public void ToQuerySelector_BuildsAProjection()
    {
        var selector = new SignalComputedQuery("{ name }").ToQuerySelector<Person>();

        selector(new Person()).ShouldBeAssignableTo<Dictionary<string, object?>>();
    }

    [Fact]
    public void ToQuerySelectorExpression_ReturnsAnUncompiledTree()
    {
        var expression = new SignalComputedQuery("{ name }").ToQuerySelectorExpression<Person>();

        expression.Parameters.Count.ShouldBe(1);
        expression.Parameters[0].Type.ShouldBe(typeof(Person));
        expression.ReturnType.ShouldBe(typeof(object));
    }
}
