using KurrentDB.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class KurrentOwnedStreamCleanup
{
    internal static async Task<KurrentCleanupDiagnostic> RunAsync(KurrentDBClient? writer, string[] streams,
        IReadOnlyList<KurrentDBClient> nativeClients, IReadOnlyList<HttpClient> httpClients, CancellationToken token, IOptions<ComparisonLifecycleOptions> options)
    {
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(nativeClients);
        ArgumentNullException.ThrowIfNull(httpClients);
        using var cleanup = new KurrentCleanupOperation(streams, token, options);
        KurrentCleanupDiagnostic diagnostic;
        try
        {
            await cleanup.DeleteAsync(writer);
        }
        finally
        {
            var disposals = cleanup.StartDisposals(nativeClients, httpClients);
            diagnostic = await cleanup.FinishAsync(disposals, Task.CompletedTask);
        }
        return diagnostic;
    }
}
