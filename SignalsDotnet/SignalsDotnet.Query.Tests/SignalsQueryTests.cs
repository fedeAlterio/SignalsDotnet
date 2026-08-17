using Shouldly;

namespace SignalsDotnet.Query.Tests;

public class SignalsQueryTests
{
    [Fact]
    public void Constructor_ParsesTheQuery()
    {
        new SignalsQuery("{ Name Age }").Fields.Select(x => x.Name).ShouldBe(["Name", "Age"]);
    }

    [Fact]
    public void Text_PreservesTheOriginalQuery()
    {
        new SignalsQuery("{ Name }").Text.ShouldBe("{ Name }");
    }

    [Fact]
    public void MalformedQuery_ThrowsOnConstruction()
    {
        Should.Throw<FormatException>(() => new SignalsQuery("{ Name"));
    }

    [Fact]
    public void NullQuery_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new SignalsQuery(null!));
    }

    [Fact]
    public void StringLiteral_ConvertsImplicitly()
    {
        SignalsQuery query = "{ Name }";

        query.Fields.Count.ShouldBe(1);
    }

    [Fact]
    public void TryParse_ReturnsTheQueryWhenValid()
    {
        SignalsQuery.TryParse("{ Name }", out var query).ShouldBeTrue();
        query!.Text.ShouldBe("{ Name }");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ Name")]
    public void TryParse_FailsWithoutThrowing(string? query)
    {
        SignalsQuery.TryParse(query, out _).ShouldBeFalse();
    }

    [Fact]
    public void Parse_IsEquivalentToTheConstructor()
    {
        SignalsQuery.Parse("{ Name }").ShouldBe(new SignalsQuery("{ Name }"));
    }

    [Fact]
    public void Equality_IsStructuralAcrossFormatting()
    {
        new SignalsQuery("{ Name, Age }").ShouldBe(new SignalsQuery("Name\nAge"));
    }

    [Fact]
    public void Equality_IsStructuralForNestedQueries()
    {
        new SignalsQuery("{ Home { City Zip } }").ShouldBe(new SignalsQuery("Home{City,Zip}"));
    }

    [Fact]
    public void DifferentNestedSelections_AreNotEqual()
    {
        new SignalsQuery("{ Home { City } }").ShouldNotBe(new SignalsQuery("{ Home { Zip } }"));
    }

    [Fact]
    public void EqualQueries_ShareAHashCode()
    {
        new SignalsQuery("{ Home { City } }").GetHashCode()
            .ShouldBe(new SignalsQuery("Home{City}").GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsTheOriginalQuery()
    {
        new SignalsQuery("{ Name }").ToString().ShouldBe("{ Name }");
    }

    [Fact]
    public void Equals_HandlesNullAndForeignTypes()
    {
        var query = new SignalsQuery("{ Name }");

        query.Equals((SignalsQuery?)null).ShouldBeFalse();
        query.Equals((object?)"{ Name }").ShouldBeFalse();
    }

    [Fact]
    public void EqualityOperators_AreStructuralAndNullSafe()
    {
        (new SignalsQuery("{ Home { City } }") == new SignalsQuery("Home{City}")).ShouldBeTrue();
        (new SignalsQuery("{ Name }") != new SignalsQuery("{ Age }")).ShouldBeTrue();

        SignalsQuery? nothing = null;
        (nothing == null).ShouldBeTrue();
        (new SignalsQuery("{ Name }") == null).ShouldBeFalse();
    }

    [Fact]
    public void SameInstance_EqualsItself()
    {
        var query = new SignalsQuery("{ Name }");

        query.Equals(query).ShouldBeTrue();
    }

    sealed class Person
    {
        public string Name { get; set; } = "Ada";
    }

    [Fact]
    public void ToQuerySelector_BuildsAProjection()
    {
        var selector = new SignalsQuery("{ name }").ToQuerySelector<Person>();

        selector(new Person()).ShouldBeAssignableTo<Dictionary<string, object?>>();
    }

    [Fact]
    public void ToQuerySelectorExpression_ReturnsAnUncompiledTree()
    {
        var expression = new SignalsQuery("{ name }").ToQuerySelectorExpression<Person>();

        expression.Parameters.Count.ShouldBe(1);
        expression.Parameters[0].Type.ShouldBe(typeof(Person));
        expression.ReturnType.ShouldBe(typeof(object));
    }
}
