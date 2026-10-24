using System.Globalization;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

/// <summary>Frozen document execution budgets and the actual client configuration.</summary>
internal static class DocumentResourceBounds
{
    internal const string PostgresConnectionsEvidence = "Benchmarks__Deployment__DocumentPostgresMaximumConnections";
    private const string ScanRecords = "KeyLoad__DatabaseLimits__MaxScanRecords";
    private const string QueryReadBytes = "KeyLoad__DatabaseLimits__MaxQueryReadBytes";
    private const string RequestsEvidence = "Benchmarks__Deployment__DocumentKeyLoadRequestsPerScope";
    private const string ScanEvidence = "Benchmarks__Deployment__DocumentKeyLoadMaximumScanRecords";
    private const string ReadEvidence = "Benchmarks__Deployment__DocumentKeyLoadMaximumQueryReadBytes";

    internal static void ApplyKeyLoad(IResourceBuilder<ProjectResource> node, BenchmarkDeploymentOptions deployment)
    {
        node.WithEnvironment(ScanRecords, deployment.DocumentKeyLoadMaximumScanRecords.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment(QueryReadBytes, deployment.DocumentKeyLoadMaximumQueryReadBytes.ToString(CultureInfo.InvariantCulture));
    }

    internal static void ApplyClient(LoadTestClientSettings client, BenchmarkDeploymentOptions deployment)
    {
        client.Set(RequestsEvidence, deployment.DocumentKeyLoadRequestsPerScope.ToString(CultureInfo.InvariantCulture));
        client.Set(ScanEvidence, deployment.DocumentKeyLoadMaximumScanRecords.ToString(CultureInfo.InvariantCulture));
        client.Set(ReadEvidence, deployment.DocumentKeyLoadMaximumQueryReadBytes.ToString(CultureInfo.InvariantCulture));
    }
}
