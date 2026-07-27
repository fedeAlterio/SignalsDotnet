using System.ComponentModel;
using R3;
using SignalsDotnet.Configuration;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet.Internals;

internal class FromObservableSignalRefCounted<T> : ISignal<T>, IEquatable<FromObservableSignalRefCounted<T?>>
{
    readonly Observable<T> _observable;
    readonly ReadonlySignalConfiguration<T?> _configuration;

    int _subscriberCount;
    IDisposable? _upstreamSubscription;
    readonly object _lock = new();

#pragma warning disable CS8618
    internal FromObservableSignalRefCounted(Observable<T> observable, ReadonlySignalConfiguration<T?> configuration)
#pragma warning restore CS8618
    {
        _observable = observable;
        _configuration = configuration;
    }

    /// <summary>
    /// Don't inline this function with a lambda
    /// </summary>
    void SetValue(T value) => Value = value;

    T _value;
    public T Value
    {
        get => Signal.GetValue(this, in _value);
        set
        {
            if (_configuration.RaiseOnlyWhenChanged && _configuration.Comparer.Equals(_value, value))
                return;

            _value = value;

            var propertyChanged = _propertyChangedCore;
            if (propertyChanged is null)
                return;

            using (Signal.UntrackedScope())
                propertyChanged(this, Signal.PropertyChangedArgs);
        }
    }

   
    PropertyChangedEventHandler? _propertyChangedCore;

    public T UntrackedValue => _value;
    object? IReadOnlySignal.UntrackedValue => UntrackedValue;

    IDisposable CreateUpstreamSubscription()
    {
        if (_configuration.SubscribeWeakly)
            return _observable.SubscribeWeakly(SetValue);
        return _observable.Subscribe(SetValue);
    }

    void OnSubscribe()
    {
        lock (_lock)
        {
            if (++_subscriberCount == 1)
                _upstreamSubscription = CreateUpstreamSubscription();
        }
    }

    void OnUnsubscribe()
    {
        lock (_lock)
        {
            if (--_subscriberCount == 0)
            {
                _upstreamSubscription?.Dispose();
                _upstreamSubscription = null;
            }
        }
    }

    public Observable<T> Values =>
        new RefCountObservable<T>(new RawPropertyChangedObservable<T>(this, false), OnSubscribe, OnUnsubscribe, activateUpstreamFirst: true);

    public Observable<T> FutureValues =>
        new RefCountObservable<T>(new RawPropertyChangedObservable<T>(this, true), OnSubscribe, OnUnsubscribe, activateUpstreamFirst: false);

    public bool Equals(FromObservableSignalRefCounted<T?>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return _configuration.Comparer.Equals(_value, other._value);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((FromObservableSignalRefCounted<T?>)obj);
    }

    public static bool operator ==(FromObservableSignalRefCounted<T> a, FromObservableSignalRefCounted<T> b) => Equals(a, b);
    public static bool operator !=(FromObservableSignalRefCounted<T> a, FromObservableSignalRefCounted<T> b) => !(a == b);

    public override int GetHashCode() => _value is null ? 0 : _configuration.Comparer.GetHashCode(_value);

    public event PropertyChangedEventHandler? PropertyChanged
    {
        // A direct PropertyChanged subscriber bypasses Values/FutureValues entirely (e.g. WPF
        // bindings via INotifyPropertyChanged), so it must ref-count the upstream itself here -
        // otherwise the upstream observable is never activated and the value never updates.
        add
        {
            _propertyChangedCore += value;
            OnSubscribe();
        }
        remove
        {
            _propertyChangedCore -= value;
            OnUnsubscribe();
        }
    }

    Observable<Unit> IReadOnlySignal.Values =>
        new RefCountObservable<Unit>(new RawPropertyChangedObservableUnit(this, false), OnSubscribe, OnUnsubscribe, activateUpstreamFirst: true);

    Observable<Unit> INotifySignalChanged.FutureValues =>
        new RefCountObservable<Unit>(new RawPropertyChangedObservableUnit(this, true), OnSubscribe, OnUnsubscribe, activateUpstreamFirst: false);


    sealed class RefCountObservable<TItem>(Observable<TItem> inner, Action onSubscribe, Action onUnsubscribe, bool activateUpstreamFirst) : Observable<TItem>
    {
        protected override IDisposable SubscribeCore(Observer<TItem> observer)
        {
            if (activateUpstreamFirst)
            {
                // Values: activate the upstream first so a synchronously replayed value updates the
                // current value before it is emitted once on subscription (no spurious leading default).
                onSubscribe();
                return new RefCountDisposable(inner.Subscribe(observer.OnNext, observer.OnErrorResume, observer.OnCompleted), onUnsubscribe);
            }

            // FutureValues: attach the handler first so a value replayed synchronously while
            // activating the upstream is delivered as a future value instead of lost.
            var innerSubscription = inner.Subscribe(observer.OnNext, observer.OnErrorResume, observer.OnCompleted);
            onSubscribe();
            return new RefCountDisposable(innerSubscription, onUnsubscribe);
        }
    }

    sealed class RefCountDisposable(IDisposable inner, Action onUnsubscribe) : IDisposable
    {
        int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                inner.Dispose();
                onUnsubscribe();
            }
        }
    }

    // Subscribes directly to _propertyChangedCore rather than the public PropertyChanged event,
    // so building Values/FutureValues doesn't ref-count the upstream a second time.
    sealed class RawPropertyChangedObservable<TValue>(FromObservableSignalRefCounted<TValue> signal, bool futureChangesOnly) : Observable<TValue>
    {
        protected override IDisposable SubscribeCore(Observer<TValue> observer)
        {
            void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => observer.OnNext(signal.Value);

            if (!futureChangesOnly)
                observer.OnNext(signal.Value);

            signal._propertyChangedCore += OnPropertyChanged;
            return new UnsubscribeDisposable(() => signal._propertyChangedCore -= OnPropertyChanged);
        }
    }

    sealed class RawPropertyChangedObservableUnit(FromObservableSignalRefCounted<T> signal, bool futureChangesOnly) : Observable<Unit>
    {
        protected override IDisposable SubscribeCore(Observer<Unit> observer)
        {
            void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => observer.OnNext(Unit.Default);

            if (!futureChangesOnly)
                observer.OnNext(Unit.Default);

            signal._propertyChangedCore += OnPropertyChanged;
            return new UnsubscribeDisposable(() => signal._propertyChangedCore -= OnPropertyChanged);
        }
    }

    sealed class UnsubscribeDisposable(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}

internal class FromObservableAsyncSignalRefCounted<T> : FromObservableSignalRefCounted<T>, IAsyncSignal<T>
{
    internal FromObservableAsyncSignalRefCounted(Observable<T> observable,
                                                 IReadOnlySignal<bool> isExecuting,
                                                 ReadonlySignalConfiguration<T?> configuration) : base(observable, configuration)
    {
        IsComputing = isExecuting;
    }

    public IReadOnlySignal<bool> IsComputing { get; }
}
