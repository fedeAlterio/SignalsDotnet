using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace SignalsDotnet.Internals.Helpers;

class ObservablesByKey<TKey, TValue> where TKey : notnull
{
    readonly ConcurrentDictionary<TKey, ISubject<TValue>> _eventHandlersByKey = new();
    readonly ConcurrentQueue<Func<IObservable<TValue>, IObservable<TValue>>> _mappers= new();

    public void Invoke(TKey key, TValue value)
    {
        if (_eventHandlersByKey.TryGetValue(key, out var handler))
            handler.OnNext(value);
    }

    public ObservablesByKey<TKey, TValue> ForEach(Func<IObservable<TValue>, IObservable<TValue>> observableMapper)
    {
        _mappers.Enqueue(observableMapper);
        return this;
    }

    public IObservable<TValue> WhenAny(params TKey[] keys)
    {
        return keys.Select(When).Merge();
    }

    public IObservable<TValue> When(TKey key)
    {
        var currentHandler = _eventHandlersByKey.GetOrAdd(key, static _ => Subject.Synchronize(new Subject<TValue>()));

        IObservable<TValue> finalObservable = currentHandler;
        foreach (var mapper in _mappers)
            finalObservable = mapper(currentHandler);

        return finalObservable;
    }
}