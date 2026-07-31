using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using R3Async;
using R3Async.Subjects;

namespace SignalsDotnet.SignalsStore.SignalR;

public static class SubjectStoreHubEx
{
    /// <summary>
    /// Maps a <see cref="SubjectStoreHub"/> at <paramref name="path"/>, backed by whatever
    /// <see cref="ISharedSubjectStore"/> <paramref name="storeFactory"/> resolves - it is invoked once
    /// per hub method call with the connection's <see cref="IServiceProvider"/>, so it can pull an
    /// already-registered store from DI or construct one directly; either way, nothing needs to be
    /// registered in <c>IServiceCollection</c> specifically for this hub.
    /// </summary>
    public static HubEndpointConventionBuilder MapSubjectStoreHub(this IEndpointRouteBuilder endpoints,
                                                                  string path,
                                                                  Func<IServiceProvider, ISharedSubjectStore> storeFactory)
    {
        if (storeFactory is null) throw new ArgumentNullException(nameof(storeFactory));

        return endpoints.MapHub<SubjectStoreHub>(path)
                         .WithMetadata(new SubjectStoreFactoryMetadata(storeFactory));
    }

    /// <summary>
    /// Maps a <see cref="SubjectStoreHub"/> backed by a plain <see cref="ISubjectStore"/>, adapted to
    /// <see cref="ISharedSubjectStore"/> so the hub has a single code path. Nothing is shared or
    /// ref-counted: every call creates a subject through the underlying store, and disposing the
    /// returned reference does nothing.
    /// </summary>
    public static HubEndpointConventionBuilder MapSubjectStoreHub(this IEndpointRouteBuilder endpoints,
                                                                  string path,
                                                                  Func<IServiceProvider, ISubjectStore> storeFactory)
    {
        if (storeFactory is null) throw new ArgumentNullException(nameof(storeFactory));

        return endpoints.MapSubjectStoreHub(path, services => new UnsharedSubjectStore(storeFactory(services)));
    }

    sealed class UnsharedSubjectStore(ISubjectStore upstream) : ISharedSubjectStore
    {
        public ValueTask<IAsyncDisposableReference<ISubject<T>>> GetOrCreateSubjectAsync<T>(string id, CancellationToken cancellationToken)
        {
            IAsyncDisposableReference<ISubject<T>> reference = new Reference<T>(upstream.CreateSubject<T>(id));
            return new ValueTask<IAsyncDisposableReference<ISubject<T>>>(reference);
        }

        sealed class Reference<T>(ISubject<T> value) : IAsyncDisposableReference<ISubject<T>>
        {
            public ISubject<T> Value => value;

            public ValueTask DisposeAsync() => default;
        }
    }
}