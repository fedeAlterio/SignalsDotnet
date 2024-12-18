using System.ComponentModel;

namespace SignalsDotnet.Internals.Helpers;

internal static class ObservableFromPropertyChanged
{
    public static FromPropertyChangedObservable<T> OnPropertyChanged<T>(this IReadOnlySignal<T> @this)
    {
        return new FromPropertyChangedObservable<T>(@this);
    }

    public readonly struct FromPropertyChangedObservable<T> : IObservable<T>
    {
        readonly IReadOnlySignal<T> _signal;

        public FromPropertyChangedObservable(IReadOnlySignal<T> signal)
        {
            _signal = signal;
        }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            return new FromPropertyChangedSubscription(observer, this);
        }

        class FromPropertyChangedSubscription : IDisposable
        {
            readonly IObserver<T> _observer;
            readonly FromPropertyChangedObservable<T> _observable;
            readonly object _locker = new();
            bool _isDisposed;

            public FromPropertyChangedSubscription(IObserver<T> observer, FromPropertyChangedObservable<T> observable)
            {
                _observer = observer;
                _observable = observable;
                lock (_locker)
                {
                    _observer.OnNext(_observable._signal.Value);
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
                lock (_locker)
                {
                    if (_isDisposed)
                        return;

                    _isDisposed = true;
                }

                _observable._signal.PropertyChanged -= OnPropertyChanged;
            }
        }
    }
}