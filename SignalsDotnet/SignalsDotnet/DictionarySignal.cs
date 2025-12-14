using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using R3;

namespace SignalsDotnet;

public class DictionarySignal<TKey, TValue> : IDictionary<TKey, TValue> where TKey : notnull
{
    readonly Dictionary<TKey, ISignal<TValue>> _valuesByKey = new();
    internal readonly Dictionary<TKey, RemoveKeyOnZeroSubscriptionsSignal> KeySignals = new();
    readonly ISignal<Unit> _keysChanged = Signal.CreateEvent();
    readonly ISignal<Unit> _valuesChanged = Signal.CreateEvent();

    public TValue this[TKey key]
    {
        get
        {
            TrackKey(key);
            var signal = _valuesByKey[key];
            return signal.Value;
        }

        set
        {
            ref var signal = ref CollectionsMarshal.GetValueRefOrAddDefault(_valuesByKey, key, out var exists);
            if (exists)
            {
                signal!.Value = value;
                _valuesChanged.Invoke();
            }
            else
            {
                var somethingOnKeyChanged = GetKeySignalOrDefault(key);
                signal = new Signal<TValue>(value);
                _keysChanged.Invoke();
                _valuesChanged.Invoke();
                somethingOnKeyChanged?.Invoke();
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    RemoveKeyOnZeroSubscriptionsSignal? GetKeySignalOrDefault(TKey key) => KeySignals.Remove(key, out var signal) ? signal : null;

    public bool Remove(TKey key)
    {
        var removed = _valuesByKey.Remove(key);
        if (removed)
        {
            _keysChanged.Invoke();
            _valuesChanged.Invoke();
            if (KeySignals.Remove(key, out var somethingOnKeyChanged))
            {
                somethingOnKeyChanged.Invoke();
            }
        }

        return removed;
    }

    public bool ContainsKey(TKey key)
    {
        TrackKey(key);
        return _valuesByKey.ContainsKey(key);
    }

    void TrackKey(TKey key)
    {
        if (!Signal.InsideComputed)
            return;

        ref var signal = ref CollectionsMarshal.GetValueRefOrAddDefault(KeySignals, key, out var exists);
        if (exists)
        {
            signal.Track();
            return;
        }

        signal = new RemoveKeyOnZeroSubscriptionsSignal(this, key);
        signal.Track();
    }

    public void Clear()
    {
        var addedOrRemovedSignals = KeySignals.Values;
        KeySignals.Clear();
        _valuesByKey.Clear();
        _keysChanged.Invoke();
        _valuesChanged.Invoke();
        foreach (var signal in addedOrRemovedSignals)
        {
            signal.Invoke();
        }
    }

    public void Add(TKey key, TValue value)
    {
        var signal = new Signal<TValue>(value);
        var keySignal = GetKeySignalOrDefault(key);
        _valuesByKey.Add(key, signal);
        keySignal?.Invoke();
        _keysChanged.Invoke();
        _valuesChanged.Invoke();
    }

    public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        TrackKey(item.Key);
        return _valuesByKey.TryGetValue(item.Key, out var signal) && EqualityComparer<TValue>.Default.Equals(signal.Value, item.Value);
    }

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        if (array == null)
            throw new ArgumentNullException(nameof(array));
        if (arrayIndex < 0 || arrayIndex > array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (array.Length - arrayIndex < Count)
            throw new ArgumentException("Array is too small");

        int index = arrayIndex;
        foreach (var kvp in this)
        {
            array[index++] = kvp;
        }
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        // Can be slightly optimized
        if (Contains(item))
        {
            return Remove(item.Key);
        }
        return false;
    }

    public int Count
    {
        get
        {
            if (Signal.InsideComputed) _keysChanged.Track();
            return _valuesByKey.Count;
        }
    }
    public bool IsReadOnly => false;

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        TrackKey(key);
        if (_valuesByKey.TryGetValue(key, out var signal))
        {
            value = signal.Value;
            return true;
        }

        value = default;
        return false;
    }

    public ICollection<TKey> Keys
    {
        get
        {
            if (Signal.InsideComputed) _keysChanged.Track();
            return _valuesByKey.Keys;
        }
    }

    public ICollection<TValue> Values
    {
        get
        {
            if (Signal.InsideComputed)
            {
                _keysChanged.Track();
                _valuesChanged.Track();
            }
            return new ValuesCollection(this);
        }
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        foreach (var kvp in _valuesByKey)
        {
            yield return new KeyValuePair<TKey, TValue>(kvp.Key, kvp.Value.Value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    sealed class ValuesCollection(DictionarySignal<TKey, TValue> dictionary) : ICollection<TValue>
    {
        public int Count => dictionary.Count;
        public bool IsReadOnly => true;

        public void Add(TValue item) => throw new NotSupportedException("Collection is read-only");
        public void Clear() => throw new NotSupportedException("Collection is read-only");
        public bool Remove(TValue item) => throw new NotSupportedException("Collection is read-only");

        public bool Contains(TValue item)
        {
            foreach (var signal in dictionary._valuesByKey.Values)
            {
                if (EqualityComparer<TValue>.Default.Equals(signal.UntrackedValue, item))
                {
                    return true;
                }
            }
            return false;
        }

        public void CopyTo(TValue[] array, int arrayIndex)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0 || arrayIndex > array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (array.Length - arrayIndex < Count)
                throw new ArgumentException("Array is too small");

            int index = arrayIndex;
            foreach (var value in this)
            {
                array[index++] = value;
            }
        }

        public IEnumerator<TValue> GetEnumerator()
        {
            foreach (var signal in dictionary._valuesByKey.Values)
            {
                yield return signal.UntrackedValue;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed class RemoveKeyOnZeroSubscriptionsSignal(DictionarySignal<TKey, TValue> dictionary, TKey key) : Signal<Unit>(static _ => Signal.SignalEventConfig)
    {
        int _subscriptionCount;
        protected internal override Observable<Unit> UntypedValues => new TrackedObservable(base.UntypedValues, this);
        protected internal override Observable<Unit> UntypedFutureValues => new TrackedObservable(base.UntypedFutureValues, this);
        void IncrementSubscriptionCount() => Interlocked.Increment(ref _subscriptionCount);
        void DecrementSubscriptionCount()
        {
            var newCount = Interlocked.Decrement(ref _subscriptionCount);
            if (newCount != 0)
                return;

            dictionary.KeySignals.Remove(key);
            dictionary._keysChanged.Invoke();
        }

        sealed class TrackedObservable(Observable<Unit> source, RemoveKeyOnZeroSubscriptionsSignal parent) : Observable<Unit>
        {
            protected override IDisposable SubscribeCore(Observer<Unit> observer)
            {
                parent.IncrementSubscriptionCount();
                var subscription = source.Subscribe(observer.OnNext, observer.OnErrorResume, observer.OnCompleted);
                return new TrackedSubscription(subscription, parent);
            }

            sealed class TrackedSubscription(IDisposable subscription, RemoveKeyOnZeroSubscriptionsSignal parent) : IDisposable
            {
                int _disposed;
                public void Dispose()
                {
                    if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1) 
                        return;

                    subscription.Dispose();
                    parent.DecrementSubscriptionCount();
                }
            }
        }
    }
}
