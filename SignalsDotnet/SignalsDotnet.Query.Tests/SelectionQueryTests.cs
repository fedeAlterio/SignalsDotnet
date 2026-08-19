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

    [Fact]
    public void NoArgumentList_IsNotACall()
    {
        SelectionQuery.Parse("{ Name }")[0].IsCall.ShouldBeFalse();
    }

    [Fact]
    public void ArgumentList_MarksTheFieldAsACall()
    {
        var field = SelectionQuery.Parse("{ Sensor(index: 0) }")[0];

        field.Name.ShouldBe("Sensor");
        field.IsCall.ShouldBeTrue();
        field.ArgumentsOrEmpty.Select(x => x.Name).ShouldBe(["index"]);
        field.ArgumentsOrEmpty[0].Value.ShouldBe(0L);
    }

    [Fact]
    public void CallWithSelectionSet_KeepsBoth()
    {
        var field = SelectionQuery.Parse("{ Sensor(index: 1) { Name Kind } }")[0];

        field.ArgumentsOrEmpty.Count.ShouldBe(1);
        field.Children.Select(x => x.Name).ShouldBe(["Name", "Kind"]);
    }

    [Theory]
    [InlineData("{ f(a: 1) }", 1L)]
    [InlineData("{ f(a: -3) }", -3L)]
    [InlineData("{ f(a: 1.5) }", 1.5)]
    [InlineData("{ f(a: -0.25) }", -0.25)]
    [InlineData("{ f(a: 1e2) }", 100d)]
    [InlineData("{ f(a: true) }", true)]
    [InlineData("{ f(a: false) }", false)]
    [InlineData("{ f(a: null) }", null)]
    [InlineData("""{ f(a: "hi") }""", "hi")]
    public void ArgumentLiterals_ParseToClrValues(string query, object? expected)
    {
        SelectionQuery.Parse(query)[0].ArgumentsOrEmpty[0].Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(@"""a\tb""", "a\tb")]
    [InlineData(@"""a\nb""", "a\nb")]
    [InlineData(@"""a\""b""", "a\"b")]
    [InlineData(@"""a\\b""", @"a\b")]
    [InlineData(@"""A""", "A")]
    public void StringArgument_UnescapesSequences(string literal, string expected)
    {
        SelectionQuery.Parse($"{{ f(a: {literal}) }}")[0].ArgumentsOrEmpty[0].Value.ShouldBe(expected);
    }

    [Fact]
    public void MultipleArguments_AreParsedInOrder()
    {
        var arguments = SelectionQuery.Parse("""{ f(a: 1, b: "two", c: true) }""")[0].ArgumentsOrEmpty;

        arguments.Select(x => x.Name).ShouldBe(["a", "b", "c"]);
        arguments.Select(x => x.Value).ShouldBe([1L, "two", true]);
    }

    [Fact]
    public void ArgumentSeparators_AreInterchangeable()
    {
        SelectionQuery.Parse("{ f(a: 1, b: 2) }").ShouldBe(SelectionQuery.Parse("{ f(a: 1 b: 2) }"));
    }

    [Fact]
    public void Alias_RenamesTheOutputKey()
    {
        var field = SelectionQuery.Parse("{ first: Sensor(index: 0) }")[0];

        field.Alias.ShouldBe("first");
        field.Name.ShouldBe("Sensor");
        field.Key.ShouldBe("first");
    }

    [Fact]
    public void WithoutAlias_TheKeyIsTheName()
    {
        SelectionQuery.Parse("{ Name }")[0].Key.ShouldBe("Name");
    }

    [Fact]
    public void Alias_WorksWithoutArguments()
    {
        var field = SelectionQuery.Parse("{ label: Name }")[0];

        field.Alias.ShouldBe("label");
        field.Name.ShouldBe("Name");
        field.IsCall.ShouldBeFalse();
    }

    [Fact]
    public void QueriesDifferingOnlyByArgumentValue_AreNotEqual()
    {
        SelectionQuery.Parse("{ f(a: 1) }").ShouldNotBe(SelectionQuery.Parse("{ f(a: 2) }"));
    }

    [Fact]
    public void QueriesDifferingOnlyByAlias_AreNotEqual()
    {
        SelectionQuery.Parse("{ x: f }").ShouldNotBe(SelectionQuery.Parse("{ y: f }"));
    }

    [Fact]
    public void IdenticalCalls_AreEqualAndShareAHashCode()
    {
        var left = SelectionQuery.Parse("""{ a: f(x: 1, y: "s") { b } }""");
        var right = SelectionQuery.Parse("""{ a: f(x: 1, y: "s") { b } }""");

        left.ShouldBe(right);
        left.Select(x => x.GetHashCode()).ShouldBe(right.Select(x => x.GetHashCode()));
    }

    [Theory]
    [InlineData("{ f() }")]
    [InlineData("{ f(a) }")]
    [InlineData("{ f(a:) }")]
    [InlineData("{ f(: 1) }")]
    [InlineData("{ f(a: 1 }")]
    [InlineData("{ f(a: 1))}")]
    [InlineData("{ f(a: 1, a: 2) }")]
    [InlineData("{ f(a: bad) }")]
    [InlineData("""{ f(a: "unterminated) }""")]
    [InlineData("{ f(a: 1.) }")]
    [InlineData("{ f(a: .5) }")]
    [InlineData("{ f(a: 1e) }")]
    [InlineData("{ : f }")]
    [InlineData("{ a: }")]
    public void MalformedCalls_Throw(string query)
    {
        Should.Throw<FormatException>(() => SelectionQuery.Parse(query));
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
