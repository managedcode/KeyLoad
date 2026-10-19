using KurrentDB.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class KurrentOwnedStreamCleanup
{
    internal static async Task<KurrentCleanupDiagnostic> RunAsync(KurrentDBClient? writer, string[] streams,
        IReadOnlyList<KurrentDBClient> runtimeClients, IReadOnlyList<HttpClient> httpClients, IOptions<ComparisonLifecycleOptions> options,
        IOptions<ComparisonDiagnosticOptions> diagnosticOptions, TimeProvider timeProvider, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(runtimeClients);
        ArgumentNullException.ThrowIfNull(httpClients);
        using var cleanup = new KurrentCleanupOperation(streams: streams, token: token, options: options, diagnosticOptions: diagnosticOptions, provider: timeProvider);
        KurrentCleanupDiagnostic diagnostic;
        try
        {
            await cleanup.DeleteAsync(writer);
        }
        finally
        {
            var disposals = cleanup.StartDisposals(runtimeClients, httpClients);
            diagnostic = await cleanup.FinishAsync(disposals, Task.CompletedTask);
        }
        return diagnostic;
    }
}
