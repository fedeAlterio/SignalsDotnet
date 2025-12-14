using System.ComponentModel;
using R3;

namespace SignalsDotnet.Internals.Helpers;

internal static class ObservableFromPropertyChanged
{
    public static FromPropertyChangedObservable<T> OnPropertyChanged<T>(this IReadOnlySignal<T> @this, bool futureChangesOnly)
    {
        return new FromPropertyChangedObservable<T>(@this, futureChangesOnly);
    }

    public static FromPropertyChangedObservableUnit OnPropertyChangedAsUnit<T>(this IReadOnlySignal<T> @this, bool futureChangesOnly)
    {
        return new FromPropertyChangedObservableUnit(@this, futureChangesOnly);
    }

    public class FromPropertyChangedObservableUnit(IReadOnlySignal signal, bool futureChangesOnly) : Observable<Unit>
    {
        readonly IReadOnlySignal _signal = signal;
        readonly bool _futureChangesOnly = futureChangesOnly;

        protected override IDisposable SubscribeCore(Observer<Unit> observer)
        {
            return new FromPropertyChangedSubscriptionUnit(observer, this);
        }


        sealed class FromPropertyChangedSubscriptionUnit : IDisposable
        {
            readonly Observer<Unit> _observer;
            readonly FromPropertyChangedObservableUnit _observable;

            public FromPropertyChangedSubscriptionUnit(Observer<Unit> observer, FromPropertyChangedObservableUnit observable)
            {
                _observer = observer;
                _observable = observable;
                if (_observable._futureChangesOnly)
                {
                    observable._signal.PropertyChanged += OnPropertyChanged;
                    return;
                }

                _observer.OnNext(Unit.Default);
                observable._signal.PropertyChanged += OnPropertyChanged;
            }

            void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => _observer.OnNext(Unit.Default);
            public void Dispose() => _observable._signal.PropertyChanged -= OnPropertyChanged;
        }
    }


    public class FromPropertyChangedObservable<T>(IReadOnlySignal<T> signal, bool futureChangesOnly) : Observable<T>
    {
        readonly IReadOnlySignal<T> _signal = signal;
        readonly bool _futureChangesOnly = futureChangesOnly;

        protected override IDisposable SubscribeCore(Observer<T> observer)
        {
            return new FromPropertyChangedSubscription(observer, this);
        }


        sealed class FromPropertyChangedSubscription : IDisposable
        {
            readonly Observer<T> _observer;
            readonly FromPropertyChangedObservable<T> _observable;

            public FromPropertyChangedSubscription(Observer<T> observer, FromPropertyChangedObservable<T> observable)
            {
                _observer = observer;
                _observable = observable;
                if (_observable._futureChangesOnly)
                {
                    observable._signal.PropertyChanged += OnPropertyChanged;
                    return;
                }

                _observer.OnNext(_observable._signal.Value);
                observable._signal.PropertyChanged += OnPropertyChanged;
            }

            void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => _observer.OnNext(_observable._signal.Value);

            public void Dispose() => _observable._signal.PropertyChanged -= OnPropertyChanged;
        }
    }
}