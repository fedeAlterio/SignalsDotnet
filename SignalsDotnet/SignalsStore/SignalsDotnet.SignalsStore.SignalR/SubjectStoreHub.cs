using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using R3Async;
using R3Async.Subjects;

namespace SignalsDotnet.SignalsStore.SignalR;

/// <summary>
/// A transparent SignalR relay in front of an existing <see cref="ISubjectStore"/>: it has no
/// opinion about replay, persistence, or completion semantics - all of that comes from whatever
/// store <see cref="SubjectStoreHubEx.MapSubjectStoreHub"/> was given. Values are forwarded as
/// opaque JSON text, since the hub cannot know the real T that each subject id was created with.
/// </summary>
public sealed class SubjectStoreHub : Hub
{
    ValueTask<IAsyncDisposableReference<ISubject<string>>> GetSubjectAsync(string id, CancellationToken cancellationToken)
    {
        var httpContext = Context.GetHttpContext() ?? throw new InvalidOperationException("No HTTP context available for this hub connection.");
        var metadata = httpContext.GetEndpoint()?.Metadata.GetMetadata<SubjectStoreFactoryMetadata>()
            ?? throw new InvalidOperationException($"No {nameof(SubjectStoreFactoryMetadata)} found on this hub's endpoint - map it with {nameof(SubjectStoreHubEx.MapSubjectStoreHub)}.");

        return metadata.ResolveSubjectStore(httpContext.RequestServices).GetOrCreateSubjectAsync<string>(id, cancellationToken);
    }

    /// <summary>
    /// Maps to <see cref="ISubject{T}.Values"/>: independently callable any number of times, each
    /// call subscribing separately to the underlying subject. The first yielded item is always a
    /// subscription ack (or failure) for the subject.Values.SubscribeAsync call itself, so the
    /// client can distinguish "subscribe failed" from "subscribed, then later errored".
    /// </summary>
    public async IAsyncEnumerable<SubjectNotification> Subscribe(string id, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var subjectReference = await GetSubjectAsync(id, cancellationToken);
        var subject = subjectReference.Value;

        var pending = Channel.CreateBounded<SubjectNotification>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        var observer = new RelayObserver(pending.Writer);

        IAsyncDisposable? subscription = null;
        Exception? subscribeError = null;
        try
        {
            subscription = await subject.Values.SubscribeAsync(observer, cancellationToken);
        }
        catch (Exception error)
        {
            subscribeError = error;
        }

        await using (subscription)
        {
            if (subscribeError is not null)
            {
                yield return SubjectNotification.SubscriptionFailed(subscribeError.Message);
                yield break;
            }

            yield return SubjectNotification.SubscriptionAcknowledged();

            await foreach (var notification in pending.Reader.ReadAllAsync(cancellationToken))
                yield return notification;
        }
    }

    /// <summary>Maps to ISubject{T}.OnNextAsync.</summary>
    public async ValueTask PublishValue(string id, string valueJson)
    {
        await using var subjectReference = await GetSubjectAsync(id, Context.ConnectionAborted);
        await subjectReference.Value.OnNextAsync(valueJson, Context.ConnectionAborted);
    }

    /// <summary>Maps to ISubject{T}.OnErrorResumeAsync.</summary>
    public async ValueTask PublishError(string id, string errorMessage)
    {
        await using var subjectReference = await GetSubjectAsync(id, Context.ConnectionAborted);
        await subjectReference.Value.OnErrorResumeAsync(new SignalRSubjectException(errorMessage), Context.ConnectionAborted);
    }

    /// <summary>Maps to ISubject{T}.OnCompletedAsync.</summary>
    public async ValueTask PublishCompleted(string id, bool success, string? errorMessage)
    {
        var result = success ? Result.Success : Result.Failure(new SignalRSubjectException(errorMessage ?? "The subject completed with an error."));
        await using var subjectReference = await GetSubjectAsync(id, Context.ConnectionAborted);
        await subjectReference.Value.OnCompletedAsync(result);
    }

    sealed class RelayObserver(ChannelWriter<SubjectNotification> writer) : AsyncObserver<string>
    {
        protected override ValueTask OnNextAsyncCore(string value, CancellationToken cancellationToken)
        {
            writer.TryWrite(SubjectNotification.ForValue(value));
            return default;
        }

        protected override ValueTask OnErrorResumeAsyncCore(Exception error, CancellationToken cancellationToken)
        {
            writer.TryWrite(SubjectNotification.ForError(error.Message));
            return default;
        }

        protected override ValueTask OnCompletedAsyncCore(Result result)
        {
            writer.TryWrite(SubjectNotification.ForCompletion(result.IsSuccess, result.IsFailure ? result.Exception.Message : null));
            writer.TryComplete();
            return default;
        }
    }
}
