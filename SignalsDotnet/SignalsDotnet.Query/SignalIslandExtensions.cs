using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using R3;

namespace SignalsDotnet.Query;

public static class SignalIslandExtensions
{
    public static async IAsyncEnumerable<object?> ReadComputedValuesAsync<T>(this SignalIsland<T> island,
                                                                           SignalsQuery query,
                                                                           JsonSerializerOptions? options = null,
                                                                           [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (island is null)
            throw new ArgumentNullException(nameof(island));

        if (query is null)
            throw new ArgumentNullException(nameof(query));

        var channel = Channel.CreateBounded<object?>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        var source = await island.SwitchToSignalContextAsync(cancellationToken);

        using var subscription = query.ComputedObservable(source, options)
                                      .Subscribe(value => channel.Writer.TryWrite(value),
                                                 error => channel.Writer.TryComplete(error),
                                                 result => channel.Writer.TryComplete(result.Exception));

        await foreach (var value in channel.Reader.ReadAllAsync(cancellationToken))
            yield return value;
    }
}
