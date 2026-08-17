using System.Net.ServerSentEvents;

namespace SignalsDotnet.Playground.Client;

sealed class DashboardStreamReader(HttpClient client)
{
    public async IAsyncEnumerable<string> ReadAsync(string query,
                                                    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var url = $"/api/dashboard/stream?query={Uri.EscapeDataString(query)}";

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        await foreach (var item in SseParser.Create(stream).EnumerateAsync(cancellationToken))
            yield return item.Data;
    }
}
