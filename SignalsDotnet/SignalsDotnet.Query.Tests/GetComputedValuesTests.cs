using System.Text.Json;
using Shouldly;

namespace SignalsDotnet.Query.Tests;

public class GetComputedValuesTests
{
    const int TestTimeoutMs = 20_000;

    static CancellationTokenSource Timeout() => new(TestTimeoutMs);

    static string Json(object? value) => JsonSerializer.Serialize(value);

    static SignalIsland<Employee> NewIsland(string name = "Ada") =>
        new(_ => new ValueTask<Employee>(new Employee
        {
            Name = name,
            Age = 36,
            Home = new Address { City = "London", Zip = "E1" }
        }));

    [Fact]
    public async Task TheFirstValue_IsTheInitialProjection()
    {
        var island = NewIsland();
        using var timeout = Timeout();

        await foreach (var value in island.ReadComputedValuesAsync(new SignalsQuery("{ name }"), cancellationToken: timeout.Token))
        {
            Json(value).ShouldBe("""{"name":"Ada"}""");
            break;
        }
    }

    [Fact]
    public async Task ChangingASelectedSignal_ProducesANewValue()
    {
        var island = NewIsland();
        using var timeout = Timeout();

        var values = island.ReadComputedValuesAsync(new SignalsQuery("{ name }"), cancellationToken: timeout.Token)
                           .GetAsyncEnumerator(timeout.Token);

        try
        {
            (await values.MoveNextAsync()).ShouldBeTrue();
            Json(values.Current).ShouldBe("""{"name":"Ada"}""");

            await island.InvokeAsync(employee => employee.Name = "Bob", timeout.Token);

            (await values.MoveNextAsync()).ShouldBeTrue();
            Json(values.Current).ShouldBe("""{"name":"Bob"}""");
        }
        finally
        {
            await values.DisposeAsync();
        }
    }

    [Fact]
    public async Task ChangingAnUnselectedSignal_ProducesNoValue()
    {
        var island = NewIsland();
        using var timeout = Timeout();

        var values = island.ReadComputedValuesAsync(new SignalsQuery("{ name }"), cancellationToken: timeout.Token)
                           .GetAsyncEnumerator(timeout.Token);

        try
        {
            (await values.MoveNextAsync()).ShouldBeTrue();

            await island.InvokeAsync(employee => employee.Age = 99, timeout.Token);
            await island.InvokeAsync(employee => employee.Name = "Bob", timeout.Token);

            (await values.MoveNextAsync()).ShouldBeTrue();
            Json(values.Current).ShouldBe("""{"name":"Bob"}""");
        }
        finally
        {
            await values.DisposeAsync();
        }
    }

    [Fact]
    public async Task ASlowConsumer_SeesOnlyTheLatestValue()
    {
        var island = NewIsland();
        using var timeout = Timeout();

        var values = island.ReadComputedValuesAsync(new SignalsQuery("{ name }"), cancellationToken: timeout.Token)
                           .GetAsyncEnumerator(timeout.Token);

        try
        {
            (await values.MoveNextAsync()).ShouldBeTrue();

            for (var i = 1; i <= 50; i++)
            {
                var captured = i;
                await island.InvokeAsync(employee => employee.Name = $"Name{captured}", timeout.Token);
            }

            (await values.MoveNextAsync()).ShouldBeTrue();
            Json(values.Current).ShouldBe("""{"name":"Name50"}""");
        }
        finally
        {
            await values.DisposeAsync();
        }
    }

    [Fact]
    public async Task NestedSelections_AreProjected()
    {
        var island = NewIsland();
        using var timeout = Timeout();

        var values = island.ReadComputedValuesAsync(new SignalsQuery("{ home { city } }"), cancellationToken: timeout.Token)
                           .GetAsyncEnumerator(timeout.Token);

        try
        {
            (await values.MoveNextAsync()).ShouldBeTrue();
            Json(values.Current).ShouldBe("""{"home":{"city":"London"}}""");

            await island.InvokeAsync(employee => employee.Home!.City = "Paris", timeout.Token);

            (await values.MoveNextAsync()).ShouldBeTrue();
            Json(values.Current).ShouldBe("""{"home":{"city":"Paris"}}""");
        }
        finally
        {
            await values.DisposeAsync();
        }
    }

    [Fact]
    public async Task Cancellation_EndsTheEnumeration()
    {
        var island = NewIsland();
        using var cancellation = new CancellationTokenSource();
        using var timeout = Timeout();

        var values = island.ReadComputedValuesAsync(new SignalsQuery("{ name }"), cancellationToken: cancellation.Token)
                           .GetAsyncEnumerator(cancellation.Token);

        try
        {
            (await values.MoveNextAsync()).ShouldBeTrue();

            await cancellation.CancelAsync();

            await Should.ThrowAsync<OperationCanceledException>(async () => await values.MoveNextAsync());
        }
        finally
        {
            await values.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposingTheEnumerator_StopsObservingSignals()
    {
        var island = NewIsland();
        using var timeout = Timeout();

        var values = island.ReadComputedValuesAsync(new SignalsQuery("{ name }"), cancellationToken: timeout.Token)
                           .GetAsyncEnumerator(timeout.Token);

        (await values.MoveNextAsync()).ShouldBeTrue();
        await values.DisposeAsync();

        await island.InvokeAsync(employee => employee.Name = "AfterDispose", timeout.Token);

        var observed = await island.InvokeAsync(employee => employee.Name, timeout.Token);
        observed.ShouldBe("AfterDispose");
    }

    [Fact]
    public async Task NullArguments_Throw()
    {
        var island = NewIsland();
        using var timeout = Timeout();

        await Should.ThrowAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in island.ReadComputedValuesAsync(null!, cancellationToken: timeout.Token))
                break;
        });

        await Should.ThrowAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in ((SignalIsland<Employee>)null!).ReadComputedValuesAsync(new SignalsQuery("{ name }"), cancellationToken: timeout.Token))
                break;
        });
    }
}
