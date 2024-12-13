using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using SignalsDotnet.Configuration;
using SignalsDotnet.Internals.Helpers;

namespace SignalsDotnet.Internals;

internal class FromObservableSignal<T> : Signal, IReadOnlySignal<T?>, IEquatable<FromObservableSignal<T?>>
{
    readonly ReadonlySignalConfiguration<T?> _configuration;
    readonly Subject<Unit> _someoneAskedValueSubject = new();
    int _someoneAskedValue; // 1 means true, 0 means false

    public FromObservableSignal(IObservable<T> observable, 
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
    void SetValue(T value) => Value = value;


    T? _value;
    public T? Value
    {
        get
        {
            NotifySomeoneAskedAValue();
            return GetValue(this, in _value);
        }
        set => SetValue(ref _value, value, _configuration.Comparer, _configuration.RaiseOnlyWhenChanged);
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

    public IDisposable Subscribe(IObserver<T?> observer) => this.OnPropertyChanged(nameof(Value), () => Value)
                                                                .Subscribe(observer);

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
    public IObservable<Unit> Changed => this.Select(static _ => Unit.Default);
}