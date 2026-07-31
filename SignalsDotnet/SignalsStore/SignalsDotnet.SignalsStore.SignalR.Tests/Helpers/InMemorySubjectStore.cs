using System.Collections.Concurrent;
using R3Async.Subjects;

namespace SignalsDotnet.SignalsStore.SignalR.Tests.Helpers;

/// <summary>
/// The simplest possible <see cref="ISubjectStore"/>: one <see cref="Subject.Create{T}"/> per id,
/// created lazily and kept for the lifetime of the store. No persistence, no cross-process fan-out -
/// good enough to exercise the hub's routing/relay logic without pulling in Redis.
/// </summary>
sealed class InMemorySubjectStore : ISubjectStore
{
    readonly ConcurrentDictionary<string, ISubject<string>> _subjectsById = new();

    public ISubject<T> CreateSubject<T>(string id)
    {
        if (typeof(T) != typeof(string))
            throw new NotSupportedException($"{nameof(InMemorySubjectStore)} only supports {nameof(String)} subjects - the hub always creates subjects of T=string.");

        var subject = _subjectsById.GetOrAdd(id, static _ => Subject.Create<string>());
        return (ISubject<T>)subject;
    }
}
