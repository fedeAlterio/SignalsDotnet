using System.Runtime.CompilerServices;
using R3;

namespace SignalsDotnet;

public static class DictionarySignalEx
{
    public static Observable<(TKey key, IReadOnlySignal<bool> isInDictionary)> KeyAddedIncludingCurrent<TKey, TValue>(this DictionarySignal<TKey, TValue> @this) where TKey : notnull
    {
        return Observable.Create<(TKey key, IReadOnlySignal<bool> isInDictionary), DictionarySignal<TKey, TValue>>(@this, static (observer, dictionary) =>
        {
            var emittedKeys = new HashSet<TKey>();
            var pendingWhileSnapshotting = new Queue<(TKey key, IReadOnlySignal<bool> isInDictionary)>();
            var isSnapshotting = new StrongBox<bool>(true);

            var subscription = dictionary.KeyAdded.Subscribe((pendingWhileSnapshotting, emittedKeys, isSnapshotting, observer), static (value, state) =>
            {
                var (queue, emitted, snapshotting, obs) = state;
                if (snapshotting.Value)
                {
                    queue.Enqueue(value);
                    return;
                }

                if (emitted.Add(value.key))
                {
                    obs.OnNext(value);
                }
            });

            foreach (var key in dictionary.CurrentKeys.ToList())
            {
                if (emittedKeys.Add(key))
                {
                    observer.OnNext((key, dictionary.KeyPresenceSignal(key)));
                }
            }

            isSnapshotting.Value = false;
            while (pendingWhileSnapshotting.Count > 0)
            {
                var value = pendingWhileSnapshotting.Dequeue();
                if (emittedKeys.Add(value.key))
                {
                    observer.OnNext(value);
                }
            }

            return subscription;
        });
    }
}
