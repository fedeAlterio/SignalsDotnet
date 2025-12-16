using System.ComponentModel;
using R3;
using SignalsDotnet.Configuration;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet.Internals;

internal class FromObservableSignal<T> : ISignal<T>, IEquatable<FromObservableSignal<T?>>
{
    readonly Observable<T> _observable;
    readonly ReadonlySignalConfiguration<T?> _configuration;
    int _someoneAskedValue; // 1 means true, 0 means false

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public FromObservableSignal(Observable<T> observable,
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
                                ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        _observable = observable ?? throw new ArgumentNullException(nameof(observable));

        var options = ReadonlySignalConfiguration<T?>.Default;
        if (configuration != null)
            options = configuration(options);

        _configuration = options;
    }


    /// <summary>
    /// Don't inline this function with a lambda
    /// </summary>
    void SetValue(T value)
    {
        Value = value;
    }

    T _value;
    public T Value
    {
        get
        {
            NotifySomeoneAskedAValue();
            return Signal.GetValue(this, in _value);
        }
        set
        {
            if (_configuration.RaiseOnlyWhenChanged && _configuration.Comparer.Equals(_value, value))
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

    public T UntrackedValue => _value;
    object? IReadOnlySignal.UntrackedValue => UntrackedValue;

    void NotifySomeoneAskedAValue()
    {
        var someoneAlreadyAskedValue = Interlocked.Exchange(ref _someoneAskedValue, 1) == 1;
        if (someoneAlreadyAskedValue)
            return;

        if (_configuration.SubscribeWeakly)
            _observable.SubscribeWeakly(SetValue);
        else
            _observable.Subscribe(SetValue);
    }

    public Observable<T> Values => this.OnPropertyChanged(false);
    public Observable<T> FutureValues => this.OnPropertyChanged(true);

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
    public event PropertyChangedEventHandler? PropertyChanged;

    Observable<Unit> IReadOnlySignal.Values => this.OnPropertyChangedAsUnit(false);
    Observable<Unit> IReadOnlySignal.FutureValues => this.OnPropertyChangedAsUnit(true);
}

internal class FromObservableAsyncSignal<T> : FromObservableSignal<T>, IAsyncSignal<T>
{
    public FromObservableAsyncSignal(Observable<T> observable,
                                     IReadOnlySignal<bool> isExecuting,
                                     ReadonlySignalConfigurationDelegate<T?>? configuration = null) : base(observable, configuration)
    {
        IsComputing = isExecuting;
    }

    public IReadOnlySignal<bool> IsComputing { get; }
}