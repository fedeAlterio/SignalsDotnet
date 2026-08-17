using Microsoft.Extensions.DependencyInjection;
using SignalsDotnet.Query;

namespace SignalsDotnet.AspNetCore;

public static class SignalIslandServiceCollectionExtensions
{
    public static IServiceCollection AddSingletonSignalIsland<T>(this IServiceCollection services) where T : class
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddSingleton(CreateIsland<T>);
    }

    public static IServiceCollection AddScopedSignalIsland<T>(this IServiceCollection services) where T : class
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddScoped(CreateIsland<T>);
    }

    public static IServiceCollection AddTransientSignalIsland<T>(this IServiceCollection services) where T : class
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddTransient(CreateIsland<T>);
    }

    public static IServiceCollection AddSingletonSignalIsland<T>(this IServiceCollection services, Func<IServiceProvider, T> factory) where T : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        return services.AddSingleton(provider => CreateIsland(provider, factory));
    }

    public static IServiceCollection AddScopedSignalIsland<T>(this IServiceCollection services, Func<IServiceProvider, T> factory) where T : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        return services.AddScoped(provider => CreateIsland(provider, factory));
    }

    public static IServiceCollection AddTransientSignalIsland<T>(this IServiceCollection services, Func<IServiceProvider, T> factory) where T : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        return services.AddTransient(provider => CreateIsland(provider, factory));
    }

    static SignalIsland<T> CreateIsland<T>(IServiceProvider provider) where T : class =>
        new(_ => new ValueTask<T>(ActivatorUtilities.CreateInstance<T>(provider)));

    static SignalIsland<T> CreateIsland<T>(IServiceProvider provider, Func<IServiceProvider, T> factory) where T : class =>
        new(_ => new ValueTask<T>(factory(provider)));
}
