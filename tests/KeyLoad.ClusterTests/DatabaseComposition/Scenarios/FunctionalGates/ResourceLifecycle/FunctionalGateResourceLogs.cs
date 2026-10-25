using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.DatabaseComposition;

/// <summary>Writes joined execution resource logs and the original retained startup snapshot without consulting a stopped provider.</summary>
internal static class FunctionalGateResourceLogs
{
    internal static async Task JoinAsync(ClusterFixtureDiagnostics? diagnostics, string[] nodeNames,
        string[] diagnosticSecrets, string root, Action<string> saveStartupEvidence, List<Exception> failures)
    {
        if (diagnostics is { } logs)
        {
            try
            {
                await diagnostics.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            ServerFailureObserver.Observe(() => FunctionalGateResourceLogs.Save(
                logs, nodeNames, diagnosticSecrets, root, saveStartupEvidence), failures);
        }
    }

    internal static void Save(ClusterFixtureDiagnostics logs, string[] nodeNames, string[] diagnosticSecrets,
        string root, Action<string> saveStartupEvidence)
    {
        ArgumentNullException.ThrowIfNull(saveStartupEvidence);
        var lines = logs.SnapshotResourceLogs(nodeNames);
        foreach (var secret in diagnosticSecrets.Where(secret => !string.IsNullOrEmpty(secret)))
        {
            lines = lines.Select(line => line.Replace(secret, "[redacted]", StringComparison.Ordinal)).ToArray();
        }
        var output = Path.Combine(root, "execution-resource-logs");
        Directory.CreateDirectory(output);
        _ = ClusterFailureReceipts.SaveImmutable(output, BoundedDiagnosticLog.Bound(lines));
        saveStartupEvidence(output);
    }
}
