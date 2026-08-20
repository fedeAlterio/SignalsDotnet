using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Shouldly;
using SignalsDotnet.Query;

namespace SignalsDotnet.AspNetCore.Tests;

public class SignalComputedTypedResultsExtensionsTests
{
    sealed class Model
    {
        public string Name { get; set; } = "Ada";

        [SignalQueryable]
        public ValueTask<string> GreetAsync(string prefix) => new($"{prefix}{Name}");

        [SignalQueryable]
        public Task<Model?> SelfAsync() => Task.FromResult<Model?>(this);
    }

    static SignalIsland<Model> NewIsland() => new(_ => new ValueTask<Model>(new Model()));

    static IResult Subscribe(string query) =>
        TypedResults.SignalIslandComputed(NewIsland(), new SignalComputedQuery(query));

    [Fact]
    public void SyncQuery_IsAccepted()
    {
        Subscribe("{ name }").ShouldNotBeOfType<BadRequest<object>>();
    }

    [Fact]
    public void AsyncQuery_IsAccepted()
    {
        Subscribe("{ greetAsync(prefix: \"Hi \") }").ShouldNotBeOfType<BadRequest<object>>();
    }

    [Fact]
    public void AsyncQuery_WithSelectionSet_IsAccepted()
    {
        Subscribe("{ selfAsync { name } }").ShouldNotBeOfType<BadRequest<object>>();
    }

    [Fact]
    public void UnknownField_IsRejected()
    {
        Subscribe("{ nope }").ShouldBeOfType<BadRequest<object>>();
    }
}
