using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SignalsDotnet.AspNetCore;
using SignalsDotnet.Query;

namespace SignalsDotnet.AspNetCore.Tests;

public class SignalIslandServiceCollectionExtensionsTests
{
    const int TestTimeoutMs = 20_000;

    static CancellationTokenSource Timeout() => new(TestTimeoutMs);

    sealed class Dependency
    {
        public int Value => 42;
    }

    sealed class Model
    {
        public Model(Dependency dependency) => Dependency = dependency;

        public Dependency Dependency { get; }
        public int Value { get; set; }
    }

    sealed class Plain;

    [Fact]
    public void Singleton_RegistersOnlyTheIsland()
    {
        var services = new ServiceCollection();
        services.AddSingletonSignalIsland<Plain>();

        services.Select(x => x.ServiceType).ShouldBe([typeof(SignalIsland<Plain>)]);
    }

    [Fact]
    public void Singleton_UsesSingletonLifetime()
    {
        var services = new ServiceCollection();
        services.AddSingletonSignalIsland<Plain>();

        services.Single().Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void Scoped_UsesScopedLifetime()
    {
        var services = new ServiceCollection();
        services.AddScopedSignalIsland<Plain>();

        services.Single().Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Transient_UsesTransientLifetime()
    {
        var services = new ServiceCollection();
        services.AddTransientSignalIsland<Plain>();

        services.Single().Lifetime.ShouldBe(ServiceLifetime.Transient);
    }

    [Fact]
    public void TheModelType_IsNotResolvable()
    {
        var services = new ServiceCollection();
        services.AddSingletonSignalIsland<Plain>();

        using var provider = services.BuildServiceProvider();

        provider.GetService<Plain>().ShouldBeNull();
        provider.GetService<SignalIsland<Plain>>().ShouldNotBeNull();
    }

    [Fact]
    public async Task TheIsland_ActivatesTheModelWithInjectedDependencies()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Dependency>();
        services.AddSingletonSignalIsland<Model>();

        using var provider = services.BuildServiceProvider();
        using var timeout = Timeout();

        var island = provider.GetRequiredService<SignalIsland<Model>>();

        var observed = 0;
        await island.InvokeAsync(model => observed = model.Dependency.Value, timeout.Token);

        observed.ShouldBe(42);
    }

    [Fact]
    public void Singleton_ResolvesTheSameIslandEveryTime()
    {
        var services = new ServiceCollection();
        services.AddSingletonSignalIsland<Plain>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<SignalIsland<Plain>>()
                .ShouldBeSameAs(provider.GetRequiredService<SignalIsland<Plain>>());
    }

    [Fact]
    public void Transient_ResolvesANewIslandEveryTime()
    {
        var services = new ServiceCollection();
        services.AddTransientSignalIsland<Plain>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<SignalIsland<Plain>>()
                .ShouldNotBeSameAs(provider.GetRequiredService<SignalIsland<Plain>>());
    }

    [Fact]
    public void Scoped_ResolvesOneIslandPerScope()
    {
        var services = new ServiceCollection();
        services.AddScopedSignalIsland<Plain>();

        using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var withinScope = first.ServiceProvider.GetRequiredService<SignalIsland<Plain>>();

        withinScope.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<SignalIsland<Plain>>());
        withinScope.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<SignalIsland<Plain>>());
    }

    [Fact]
    public async Task Scoped_ActivatesTheModelFromTheOwningScope()
    {
        var services = new ServiceCollection();
        services.AddScoped<Dependency>();
        services.AddScopedSignalIsland<Model>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var timeout = Timeout();

        var island = scope.ServiceProvider.GetRequiredService<SignalIsland<Model>>();

        Dependency? fromIsland = null;
        await island.InvokeAsync(model => fromIsland = model.Dependency, timeout.Token);

        fromIsland.ShouldBeSameAs(scope.ServiceProvider.GetRequiredService<Dependency>());
    }

    [Fact]
    public void NullServices_Throws()
    {
        Should.Throw<ArgumentNullException>(() => ((IServiceCollection)null!).AddSingletonSignalIsland<Plain>());
        Should.Throw<ArgumentNullException>(() => ((IServiceCollection)null!).AddScopedSignalIsland<Plain>());
        Should.Throw<ArgumentNullException>(() => ((IServiceCollection)null!).AddTransientSignalIsland<Plain>());
    }
}
