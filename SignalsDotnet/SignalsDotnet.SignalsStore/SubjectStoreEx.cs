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
        private static readonly BoundedChannelOptions KeepLatestBoundedChannelOptions = new (1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        };

        public ISubject<T> CreateSubject<T>(string id)
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

            var writes = new WritePipeline<T>(subject);

            return new SignalProxy<T>(id, startValue, subject.Values, writes.Push, proxyConfiguration);
        }
    }

    sealed class WritePipeline<T>
    {
        readonly R3.Subject<T> _sets = new();

        public WritePipeline(ISubject<T> subject)
        {
            var dropPrevious = BackpressureStrategy.FromBoundedChannel(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

            _ = _sets.ToAsyncObservable(dropPrevious)
                .SubscribeAsync(subject.AsAsyncObserver(), CancellationToken.None)
                .AsTask();
        }

        public void Push(T value) => _sets.OnNext(value);
    }
}