using Microsoft.AspNetCore.SignalR.Client;
using SignalsDotnet.Configuration;
using SignalsDotnet.SignalsStore.SignalR.Client;

namespace SignalsDotnet.SignalsStore.SignalR;

/// <summary>
/// An <see cref="ISignalStore"/> that talks to a <see cref="SubjectStoreHub"/> over a
/// <see cref="HubConnection"/> built lazily from a url. The connection is created and started on
/// the first subscription/write that needs it, shared across every signal proxy created from this
/// store, and stopped once the last one releases it.
/// </summary>
public sealed class SignalRSignalStore : ISignalStore
{
    readonly ISignalStore _inner;

    SignalRSignalStore(SignalRSubjectStore subjectStore, SignalProxyConfigurationDelegate? configuration)
    {
        _inner = subjectStore.ToSignalsStore(configuration);
    }

    /// <summary>
    /// Creates an <see cref="ISignalStore"/> backed by a <see cref="HubConnection"/> for
    /// <paramref name="url"/> (optionally customized via <paramref name="configureConnection"/>).
    /// Nothing is connected yet - the connection is built and started lazily on first use.
    /// </summary>
    public static SignalRSignalStore Create(string url,
                                             Action<IHubConnectionBuilder>? configureConnection = null,
                                             SignalRSubjectStoreOptions? subjectStoreOptions = null,
                                             SignalProxyConfigurationDelegate? configuration = null)
    {
        return new SignalRSignalStore(SignalRSubjectStore.Create(url, configureConnection, subjectStoreOptions), configuration);
    }

    public ISignalProxy<T> CreateSignalProxy<T>(string id, T startValue) => _inner.CreateSignalProxy(id, startValue);
}
