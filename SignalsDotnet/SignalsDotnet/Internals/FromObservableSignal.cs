using System.ComponentModel;
using R3;
using SignalsDotnet.Configuration;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet.Internals;

internal class FromObservableSignal<T> : Observable<T>, IReadOnlySignal<T?>, IEquatable<FromObservableSignal<T?>>
{
    readonly ReadonlySignalConfiguration<T?> _configuration;
    readonly Subject<Unit> _someoneAskedValueSubject = new(); // lock
    int _someoneAskedValue; // 1 means true, 0 means false

    public FromObservableSignal(Observable<T> observable,
                                ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        if (observable is null)
            throw new ArgumentNullException(nameof(observable));

        var options = ReadonlySignalConfiguration<T?>.Default;
        if (configuration != null)
            options = configuration(options);

        _configuration = options;

        _someoneAskedValueSubject.Take(1)
                                 .Subscribe(_ =>
                                 {
                                     if (_configuration.SubscribeWeakly)
                                         observable.SubscribeWeakly(SetValue);
                                     else
                                         observable.Subscribe(SetValue);
                                 });
    }


    /// <summary>
    /// Dont inline this function with a lambda
    /// </summary>
    void SetValue(T value)
    {
        Value = value;
    }

    T? _value;
    public T? Value
    {
        get
        {
            NotifySomeoneAskedAValue();
            return Signal.GetValue(this, in _value);
        }
        private set
        {
            if (EqualityComparer<T>.Default.Equals(_value, value))
                return;

            _value = value;

            var propertyChanged = PropertyChanged;
            if (propertyChanged is null)
            {
                return;
            }

            using (Signal.UntrackedScope())
            {
                propertyChanged(this, Signal.PropertyChangedArgs);
            }
        }
    }

    public T? UntrackedValue => _value;
    object? IReadOnlySignal.UntrackedValue => UntrackedValue;

    void NotifySomeoneAskedAValue()
    {
        var someoneAlreadyAskedValue = Interlocked.Exchange(ref _someoneAskedValue, 1) == 1;
        if (someoneAlreadyAskedValue)
            return;

        _someoneAskedValueSubject.OnNext(default);
        _someoneAskedValueSubject.OnCompleted();
        _someoneAskedValueSubject.Dispose();
    }

    protected override IDisposable SubscribeCore(Observer<T> observer) => this.OnPropertyChanged(false)
                                                                               .Subscribe(observer.OnNext!, observer.OnErrorResume, observer.OnCompleted);

    public bool Equals(FromObservableSignal<T?>? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return _configuration.Comparer.Equals(_value, other._value);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null)
            return false;
        if (ReferenceEquals(this, obj))
            return true;
        if (obj.GetType() != GetType())
            return false;

        return Equals((FromObservableSignal<T?>)obj);
    }

    public static bool operator ==(FromObservableSignal<T> a, FromObservableSignal<T> b) => Equals(a, b);
    public static bool operator !=(FromObservableSignal<T> a, FromObservableSignal<T> b) => !(a == b);

    public override int GetHashCode() => _value is null ? 0 : _configuration.Comparer.GetHashCode(_value);
    public Observable<Unit> ValuesUnit => this.Select(static _ => Unit.Default);
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal class FromObservableAsyncSignal<T> : FromObservableSignal<T>, IAsyncReadOnlySignal<T>
{
    public FromObservableAsyncSignal(Observable<T> observable,
                                     IReadOnlySignal<bool> isExecuting,
                                     ReadonlySignalConfigurationDelegate<T?>? configuration = null) : base(observable, configuration)
    {
        IsComputing = isExecuting;
    }

    public IReadOnlySignal<bool> IsComputing { get; }
}