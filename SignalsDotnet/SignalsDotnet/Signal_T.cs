using System.ComponentModel;
using R3;
using SignalsDotnet.Configuration;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet;

public class Signal<T> : Observable<T>, IReadOnlySignal<T?>, IEquatable<Signal<T>>
{
    readonly SignalConfiguration<T> _configuration;
    public Signal(SignalConfigurationDelegate<T?>? configurator = null) : this(default!, configurator!)
    {
    }

    public Signal(T startValue, SignalConfigurationDelegate<T>? configurator = null)
    {
        var configuration = SignalConfiguration<T>.Default;
        if (configurator != null)
            configuration = configurator(configuration);

        _configuration = configuration;
        _value = startValue;
    }


    T _value;
    public T Value
    {
        get => Signal.GetValue(this, in _value);
        set
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

    public T UntrackedValue => _value;
    object? IReadOnlySignal.UntrackedValue => UntrackedValue;

    protected override IDisposable SubscribeCore(Observer<T> observer) => this.OnPropertyChanged(false)
                                                                              .Subscribe(observer.OnNext!, observer.OnErrorResume, observer.OnCompleted);

    public bool Equals(Signal<T>? other)
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

        return Equals((Signal<T>)obj);
    }

    public static bool operator ==(Signal<T> a, Signal<T> b) => Equals(a, b);
    public static bool operator !=(Signal<T> a, Signal<T> b) => !(a == b);

    public override int GetHashCode() => _value is null ? 0 : _configuration.Comparer.GetHashCode(_value!);

    public event PropertyChangedEventHandler? PropertyChanged;
    public Observable<Unit> ValuesUnit => this.OnPropertyChangedAsUnit(false);
    public Observable<Unit> FutureValuesUnit => this.OnPropertyChangedAsUnit(true);
}