using System.Threading.Channels;
using R3Async;
using R3Async.R3Interop;
using R3Async.Subjects;
using SignalsDotnet.Configuration;

namespace SignalsDotnet.SignalsStore;

public static class SubjectStoreEx
{
    public static ISubjectStore ObserveLatestOn(this ISubjectStore subjectStore, AsyncContext? asyncContext)
    {
        if (subjectStore == null) throw new ArgumentNullException(nameof(subjectStore));
        return new SubjectStoreObserveLatestOn(subjectStore, asyncContext);
    }

    sealed class SubjectStoreObserveLatestOn(ISubjectStore upstream, AsyncContext? asyncContext) : ISubjectStore
    {
        private static readonly BoundedChannelOptions KeepLatestBoundedChannelOptions = new(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        };

        public R3Async.Subjects.ISubject<T> CreateSubject<T>(string id)
        {
            return upstream.CreateSubject<T>(id).MapValues(values =>
            {
                return AsyncObservable
                    .Using(subscriptionToken => values.SubscribeToAsyncEnumerableAsync(
                            static () => Channel.CreateBounded<T>(KeepLatestBoundedChannelOptions),
                            cancellationToken: subscriptionToken),
                        x =>
                        {
                            var ret = x.Value.ToAsyncObservable();
                            return asyncContext is null ? ret : ret.ObserveOn(asyncContext);
                        });
            });
        }
    }

    public static ISharedSubjectStore Share(this ISubjectStore subjectStore, ShareConfig? shareConfig = null)
    {
        if (subjectStore is null)
            throw new ArgumentNullException(nameof(subjectStore));

        return new SharedSubjectStore(subjectStore, shareConfig ?? ShareConfig.ResetOnCompletionAndRefCountZero);
    }

    sealed class SharedSubjectStore : ISharedSubjectStore
    {
        readonly RefCountTable<Key, object> _table;

        public SharedSubjectStore(ISubjectStore upstream, ShareConfig shareConfig)
        {
            _table = RefCountTable.Create<Key, object>(
                (key, _) => Task.FromResult(new AsyncDisposableValue<object>
                {
                    Value = key.CreateSubject(upstream, shareConfig),
                    Disposable = AsyncDisposable.Empty
                }));
        }

        public async ValueTask<IAsyncDisposableReference<ISubject<T>>> GetOrCreateSubjectAsync<T>(string id, CancellationToken cancellationToken)
        {
            if (id is null)
                throw new ArgumentNullException(nameof(id));

            var reference = await _table.GetOrCreateAsync(new Key<T>(id), cancellationToken);
            return new TypedReference<T>(reference);
        }

        abstract class Key(string id) : IEquatable<Key>
        {
            public string Id { get; } = id;

            public abstract object CreateSubject(ISubjectStore upstream, ShareConfig shareConfig);

            public bool Equals(Key? other) => other is not null && Id == other.Id;
            public override bool Equals(object? obj) => Equals(obj as Key);
            public override int GetHashCode() => Id.GetHashCode();
        }

        sealed class Key<T>(string id) : Key(id)
        {
            public override object CreateSubject(ISubjectStore upstream, ShareConfig shareConfig)
            {
                var subject = upstream.CreateSubject<T>(Id);
                var shared = subject.Values.Share(shareConfig);
                return subject.MapValues(_ => shared);
            }
        }

        sealed class TypedReference<T>(IAsyncDisposableReference<object> inner) : IAsyncDisposableReference<ISubject<T>>
        {
            public ISubject<T> Value => (ISubject<T>)inner.Value;

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    public static ISignalStore ToSignalsStore(this ISubjectStore subjectStore, SignalProxyConfigurationDelegate? configuration = null)
    {
        if (subjectStore is null)
            throw new ArgumentNullException(nameof(subjectStore));

        return new SubjectStoreSignalStore(subjectStore, configuration);
    }

    sealed class SubjectStoreSignalStore(ISubjectStore subjectStore, SignalProxyConfigurationDelegate? configuration) : ISignalStore
    {
        public ISignalProxy<T> CreateSignalProxy<T>(string id, T startValue)
        {
            // Stay in the async world: the proxy owns the connect/disconnect lifecycle and
            // only converts to an R3 Observable at the very end.
            var subject = subjectStore.CreateSubject<T>(id);

            ReadonlySignalConfigurationDelegate<T?>? proxyConfiguration = configuration is null
                ? null
                : config =>
                {
                    var nonGeneric = new ReadonlySignalConfiguration(config.RaiseOnlyWhenChanged, config.SubscribeWeakly, config.SubscriptionStrategy);
                    var updated = configuration(id, nonGeneric);
                    return config with
                    {
                        RaiseOnlyWhenChanged = updated.RaiseOnlyWhenChanged,
                        SubscribeWeakly = updated.SubscribeWeakly
                    };
                };

            return new SignalProxy<T>(id, startValue, subject, proxyConfiguration);
        }
    }
}