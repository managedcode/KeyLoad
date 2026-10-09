using System.Globalization;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

/// <summary>Frozen document-only execution budgets and original runner configuration evidence.</summary>
internal static class DocumentResourceBounds
{
    internal const string PostgresConnectionsEvidence = "Benchmarks__Deployment__DocumentPostgresMaximumConnections";
    private const string ScanRecords = "KeyLoad__DatabaseLimits__MaxScanRecords";
    private const string QueryReadBytes = "KeyLoad__DatabaseLimits__MaxQueryReadBytes";
    private const string SnapshotThreshold = "KeyLoad__SnapshotThreshold";
    private const string RequestsEvidence = "Benchmarks__Deployment__DocumentKeyLoadRequestsPerScope";
    private const string ScanEvidence = "Benchmarks__Deployment__DocumentKeyLoadMaximumScanRecords";
    private const string ReadEvidence = "Benchmarks__Deployment__DocumentKeyLoadMaximumQueryReadBytes";
    private const string SnapshotEvidence = "Benchmarks__Deployment__DocumentKeyLoadSnapshotThreshold";

    internal static void ApplyKeyLoad(IResourceBuilder<ContainerResource> node, IResourceBuilder<ContainerResource> runner,
        BenchmarkDeploymentOptions deployment)
    {
        var scans = deployment.DocumentKeyLoadMaximumScanRecords.ToString(CultureInfo.InvariantCulture);
        var bytes = deployment.DocumentKeyLoadMaximumQueryReadBytes.ToString(CultureInfo.InvariantCulture);
        var snapshot = deployment.DocumentKeyLoadSnapshotThreshold.ToString(CultureInfo.InvariantCulture);
        node.WithEnvironment(ScanRecords, scans).WithEnvironment(QueryReadBytes, bytes)
            .WithEnvironment(SnapshotThreshold, snapshot);
        runner.WithEnvironment(RequestsEvidence, deployment.DocumentKeyLoadRequestsPerScope.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment(ScanEvidence, scans).WithEnvironment(ReadEvidence, bytes)
            .WithEnvironment(SnapshotEvidence, snapshot);
    }
}
