using R3Async;
using R3Async.Subjects;

namespace SignalsDotnet.SignalsStore;

public interface ISubjectStore
{
    ISubject<T> CreateSubject<T>(string id);
}

public interface ISharedSubjectStore
{
    ValueTask<IAsyncDisposableReference<ISubject<T>>> GetOrCreateSubjectAsync<T>(string id, CancellationToken cancellationToken);
}