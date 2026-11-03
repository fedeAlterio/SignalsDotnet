using R3;

namespace SignalsDotnet.Internals;

internal sealed class DeadlineSignal(TimeSpan due, TimeProvider timeProvider) : INotifySignalChanged
{
    static readonly TimeSpan MaxTimerDue = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    public Observable<Unit> FutureValues => Observable.Timer(due < MaxTimerDue ? due : MaxTimerDue, timeProvider);
}
