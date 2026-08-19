using System.Reflection;
using Microsoft.AspNetCore.Builder;

namespace SignalsDotnet.AspNetCore;

public static class SignalIslandEndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder WithSignalIslandDiscovery(this RouteHandlerBuilder builder) =>
        builder.WithSignalIslandDiscovery(static _ => { });

    public static RouteHandlerBuilder WithSignalIslandDiscovery(this RouteHandlerBuilder builder,
                                                                Action<SignalIslandDiscoveryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SignalIslandDiscoveryOptions();
        configure(options);

        builder.Add(endpoint =>
        {
            if (SignalIslandMetadata.For(endpoint.Metadata.OfType<MethodInfo>().LastOrDefault(), options) is { } metadata)
                endpoint.Metadata.Add(metadata);
        });

        return builder;
    }
}
