using SignalsDotnet.Query.Internals;
using System.Text.Json.Nodes;
using Shouldly;

namespace SignalsDotnet.Query.Tests;

public class SelectionQueryParseTests
{
    [Fact]
    public void BracedQuery_ParsesSingleField()
    {
        var fields = SelectionQuery.Parse("{ Name }");

        fields.Count.ShouldBe(1);
        fields[0].Name.ShouldBe("Name");
        fields[0].Children.ShouldBeEmpty();
    }

    [Fact]
    public void OuterBraces_AreOptional()
    {
        SelectionQuery.Parse("Name").ShouldBe(SelectionQuery.Parse("{ Name }"));
    }

    [Fact]
    public void WhitespaceAndCommas_AreInterchangeableSeparators()
    {
        SelectionQuery.Parse("{ Name, Age }").ShouldBe(SelectionQuery.Parse("{ Name Age }"));
    }

    [Fact]
    public void Newlines_SeparateFields()
    {
        SelectionQuery.Parse("{\n  Name\n  Age\n}").ShouldBe(SelectionQuery.Parse("{ Name Age }"));
    }

    [Fact]
    public void NestedSelectionSet_BecomesChildren()
    {
        var fields = SelectionQuery.Parse("{ Name Address { City Zip } }");

        fields.Select(x => x.Name).ShouldBe(["Name", "Address"]);
        fields[0].Children.ShouldBeEmpty();
        fields[1].Children.Select(x => x.Name).ShouldBe(["City", "Zip"]);
    }

    [Fact]
    public void SelectionSets_NestArbitrarilyDeep()
    {
        var fields = SelectionQuery.Parse("{ a { b { c { d } } } }");

        var depth = 0;
        for (var current = fields; current.Count > 0; current = current[0].Children)
            depth++;

        depth.ShouldBe(4);
    }

    [Theory]
    [InlineData("_")]
    [InlineData("_x1")]
    [InlineData("Name2")]
    public void FieldNames_AllowLeadingUnderscoreAndTrailingDigits(string name)
    {
        SelectionQuery.Parse($"{{ {name} }}")[0].Name.ShouldBe(name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ }")]
    [InlineData("{ Name")]
    [InlineData("Name }")]
    [InlineData("{ Name } Age")]
    [InlineData("{ 1abc }")]
    [InlineData("Name { City")]
    [InlineData("{ Address { City }")]
    public void MalformedQueries_Throw(string query)
    {
        Should.Throw<FormatException>(() => SelectionQuery.Parse(query));
    }
}

public class SelectionQueryApplyTests
{
    const string Person = """{"Name":"Ada","Age":36,"Address":{"City":"London","Zip":"E1"},"Tags":["x","y"]}""";

    static string? Apply(string query, string json) =>
        SelectionQuery.Parse(query).Apply(JsonNode.Parse(json))?.ToJsonString();

    [Fact]
    public void SelectedField_IsProjected()
    {
        Apply("{ Name }", Person).ShouldBe("""{"Name":"Ada"}""");
    }

    [Fact]
    public void UnselectedFields_AreOmitted()
    {
        Apply("{ Age }", Person).ShouldBe("""{"Age":36}""");
    }

    [Fact]
    public void FieldOrder_FollowsTheQuery()
    {
        Apply("{ Age Name }", Person).ShouldBe("""{"Age":36,"Name":"Ada"}""");
    }

    [Fact]
    public void NestedSelection_ProjectsSubtree()
    {
        Apply("{ Address { City } }", Person).ShouldBe("""{"Address":{"City":"London"}}""");
    }

    [Fact]
    public void FieldWithoutSelectionSet_ReturnsWholeSubtree()
    {
        Apply("{ Address }", Person).ShouldBe("""{"Address":{"City":"London","Zip":"E1"}}""");
    }

    [Fact]
    public void ArrayValue_IsReturnedWholeWhenUnselected()
    {
        Apply("{ Tags }", Person).ShouldBe("""{"Tags":["x","y"]}""");
    }

    [Fact]
    public void MissingField_ProjectsToNull()
    {
        Apply("{ Nope }", Person).ShouldBe("""{"Nope":null}""");
    }

    [Fact]
    public void ArrayOfObjects_MapsSelectionOverEachElement()
    {
        var people = """[{"Name":"Ada","Age":36},{"Name":"Bob","Age":41}]""";

        Apply("{ Name }", people).ShouldBe("""[{"Name":"Ada"},{"Name":"Bob"}]""");
    }

    [Fact]
    public void NestedArrayOfObjects_MapsSelection()
    {
        var json = """{"People":[{"Name":"Ada","Age":36},{"Name":"Bob","Age":41}]}""";

        Apply("{ People { Name } }", json).ShouldBe("""{"People":[{"Name":"Ada"},{"Name":"Bob"}]}""");
    }

    [Fact]
    public void NullNode_ProjectsToNull()
    {
        SelectionQuery.Parse("{ Name }").Apply(null).ShouldBeNull();
    }

    [Fact]
    public void SelectingIntoAScalar_ProjectsToNull()
    {
        Apply("{ Name { Nested } }", Person).ShouldBe("""{"Name":null}""");
    }

    [Fact]
    public void ExplicitJsonNull_IsPreserved()
    {
        Apply("{ Name }", """{"Name":null}""").ShouldBe("""{"Name":null}""");
    }

    [Fact]
    public void Apply_DoesNotMutateTheSource()
    {
        var source = JsonNode.Parse(Person)!;

        SelectionQuery.Parse("{ Name Address { City } }").Apply(source);

        source.ToJsonString().ShouldBe(Person);
    }
}
