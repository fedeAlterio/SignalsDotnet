using System.Reactive.Linq;

namespace SignalsDotnet;

public static class CancellationSignal
{
    public static IReadOnlySignal<CancellationToken> Create(IObservable<bool> isCancelledObservable)
    {
        return isCancelledObservable.DistinctUntilChanged()
                                    .Scan((cancelationTokenSource: (CancellationTokenSource?)null, cancellationToken: default(CancellationToken)), (x, isCancelled) =>
                                     {
                                         if (isCancelled)
                                         {
                                             var cancellationTokenSource = x.cancelationTokenSource ?? new CancellationTokenSource();
                                             var token = cancellationTokenSource.Token;
                                             cancellationTokenSource.Cancel();
                                             cancellationTokenSource.Dispose();

                                             return (cancellationTokenSource, token);
                                         }

                                         var newCancellationToken = new CancellationTokenSource();
                                         return (newCancellationToken, newCancellationToken.Token);
                                     })
                                     .Select(x => x.cancellationToken)
                                     .ToSignal(x => x with { RaiseOnlyWhenChanged = false });
    }
}
