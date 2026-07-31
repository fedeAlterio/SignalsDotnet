namespace SignalsDotnet.SignalsStore.SignalR;

/// <summary>
/// Endpoint metadata carrying the <see cref="ISharedSubjectStore"/> factory for a mapped
/// <see cref="SubjectStoreHub"/> route, attached by <see cref="SubjectStoreHubEx.MapSubjectStoreHub"/>
/// and read back by the hub at connection time - avoids requiring the store to be registered in
/// the DI container.
/// </summary>
public sealed class SubjectStoreFactoryMetadata(Func<IServiceProvider, ISharedSubjectStore> storeFactory)
{
    public ISharedSubjectStore ResolveSubjectStore(IServiceProvider services) => storeFactory(services);
}
