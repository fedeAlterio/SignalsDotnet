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

    public class FromPropertyChangedObservableUnit : Observable<Unit>
    {
        readonly IReadOnlySignal _signal;
        readonly bool _futureChangesOnly;

        public FromPropertyChangedObservableUnit(IReadOnlySignal signal, bool futureChangesOnly)
        {
            _signal = signal;
            _futureChangesOnly = futureChangesOnly;
        }

        protected override IDisposable SubscribeCore(Observer<Unit> observer)
        {
            return new FromPropertyChangedSubscriptionUnit(observer, this);
        }


        class FromPropertyChangedSubscriptionUnit : IDisposable
        {
            readonly Observer<Unit> _observer;
            readonly FromPropertyChangedObservableUnit _observable;
            readonly object _locker = new();
            bool _isDisposed;

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
                lock (_locker)
                {
                    if (_isDisposed)
                    {
                        return;
                    }

                    observable._signal.PropertyChanged += OnPropertyChanged;
                }
            }

            void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                _observer.OnNext(Unit.Default);
            }

            public void Dispose()
            {
                if (!_observable._futureChangesOnly)
                {
                    lock (_locker)
                    {
                        if (_isDisposed)
                            return;

                        _isDisposed = true;
                    }
                }

                _observable._signal.PropertyChanged -= OnPropertyChanged;
            }
        }
    }


    public class FromPropertyChangedObservable<T> : Observable<T>
    {
        readonly IReadOnlySignal<T> _signal;
        readonly bool _futureChangesOnly;

        public FromPropertyChangedObservable(IReadOnlySignal<T> signal, bool futureChangesOnly)
        {
            _signal = signal;
            _futureChangesOnly = futureChangesOnly;
        }

        protected override IDisposable SubscribeCore(Observer<T> observer)
        {
            return new FromPropertyChangedSubscription(observer, this);
        }


        class FromPropertyChangedSubscription : IDisposable
        {
            readonly Observer<T> _observer;
            readonly FromPropertyChangedObservable<T> _observable;
            readonly object _locker = new();
            bool _isDisposed;

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
                lock (_locker)
                {
                    if (_isDisposed)
                    {
                        return;
                    }

                    observable._signal.PropertyChanged += OnPropertyChanged;
                }
            }

            void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                _observer.OnNext(_observable._signal.Value);
            }

            public void Dispose()
            {
                if (!_observable._futureChangesOnly)
                {
                    lock (_locker)
                    {
                        if (_isDisposed)
                            return;

                        _isDisposed = true;
                    }
                }

                _observable._signal.PropertyChanged -= OnPropertyChanged;
            }
        }
    }
}