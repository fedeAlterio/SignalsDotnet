using Microsoft.Extensions.DependencyInjection;
using SignalsDotnet.Query;

namespace SignalsDotnet.AspNetCore;

public static class SignalIslandServiceCollectionExtensions
{
    public static IServiceCollection AddSingletonSignalIsland<T>(this IServiceCollection services) where T : class
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        return services.AddSingleton(CreateIsland<T>);
    }

    public static IServiceCollection AddScopedSignalIsland<T>(this IServiceCollection services) where T : class
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        return services.AddScoped(CreateIsland<T>);
    }

    public static IServiceCollection AddTransientSignalIsland<T>(this IServiceCollection services) where T : class
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        return services.AddTransient(CreateIsland<T>);
    }

    static SignalIsland<T> CreateIsland<T>(IServiceProvider provider) where T : class =>
        new(_ => new ValueTask<T>(ActivatorUtilities.CreateInstance<T>(provider)));
}
