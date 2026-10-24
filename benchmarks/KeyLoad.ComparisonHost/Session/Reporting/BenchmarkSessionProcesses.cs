using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.Diagnostics.ResourceExecution;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

/// <summary>The actual workload client process; every database server is identified by its digest-pinned container image.</summary>
internal sealed record BenchmarkSessionProcesses(ProcessEvidence Client)
{
    private const string ClientName = "comparison-client";

    internal static BenchmarkSessionProcesses ReadCurrent() => new(ProjectProcessEvidence.ReadCurrent(ClientName));
}
