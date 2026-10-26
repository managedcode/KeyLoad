using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

internal static class NativeDatabaseTargets
{
    private const string Connection = "Benchmarks__Execution__ConnectionString", Image = "Benchmarks__Execution__Image";
    private const string Replica = "Benchmarks__Execution__ReplicaConnections__", Endpoint = "Benchmarks__Execution__Endpoints__";
    private const int SingleNode = 1;

    internal static ComparisonTopology Topology(IReadOnlyDictionary<string, string> values)
        => values.Count(pair => pair.Key.StartsWith(Endpoint, StringComparison.Ordinal)) == SingleNode
            ? ComparisonTopology.Standalone : ComparisonTopology.Replicated;

    internal static IComparisonTarget Redis(IReadOnlyDictionary<string, string> values, string runId,
        IOptions<ComparisonExecutionOptions> options, IConfiguration configuration)
        => new RedisTarget(values[Connection], runId, values[Image], ComparisonExecutionRegistration.ReadLifecycle(configuration),
            options, ComparisonExecutionRegistration.ReadDiagnostics(configuration), Topology(values),
            [.. values.Where(pair => pair.Key.StartsWith(Replica, StringComparison.Ordinal)).OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value)]);
}
