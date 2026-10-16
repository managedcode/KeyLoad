using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Writes joined native resource logs and the original retained startup snapshot without consulting a stopped provider.</summary>
internal static class FunctionalGateResourceLogs
{
    internal static void Save(ClusterFixtureDiagnostics logs, string[] nodeNames, string[] diagnosticSecrets,
        string root, Action<string> saveStartupEvidence)
    {
        ArgumentNullException.ThrowIfNull(saveStartupEvidence);
        var lines = logs.SnapshotResourceLogs(nodeNames);
        foreach (var secret in diagnosticSecrets.Where(secret => !string.IsNullOrEmpty(secret)))
        {
            lines = lines.Select(line => line.Replace(secret, "[redacted]", StringComparison.Ordinal)).ToArray();
        }
        var output = Path.Combine(root, "native-resource-logs");
        Directory.CreateDirectory(output);
        _ = ClusterFailureReceipts.SaveImmutable(output, BoundedDiagnosticLog.Bound(lines));
        saveStartupEvidence(output);
    }
}
