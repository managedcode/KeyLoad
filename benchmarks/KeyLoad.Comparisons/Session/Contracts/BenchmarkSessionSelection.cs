namespace KeyLoad.Comparisons;

/// <summary>One session's execution physical topology, independent from its workload cases.</summary>
public sealed record BenchmarkSessionSelection(string Target, int NodeCount)
{
    /// <summary>The canonical database selector.</summary>
    public const string TargetSetting = "Benchmarks:Target";
    /// <summary>The explicit execution node-count selector.</summary>
    public const string NodeCountSetting = "Benchmarks:NodeCount";
    /// <summary>Gets the selected execution topology.</summary>
    public ComparisonTopology Topology => NodeCount == BenchmarkSessionLimits.SingleNodeCount
        ? ComparisonTopology.Standalone : ComparisonTopology.Replicated;
    /// <summary>Gets the initial retained corpus size.</summary>
    public int Records => BenchmarkSessionLimits.InitialRecords;
    /// <summary>Gets the total admitted concurrent execution clients.</summary>
    public int Concurrency => BenchmarkSessionLimits.Clients;
    /// <summary>Rejects unknown target identities and noncanonical physical node counts.</summary>
    public void Validate()
    {
        if (!BenchmarkSessionContract.Targets.Contains(Target, StringComparer.Ordinal)
            || NodeCount is not (BenchmarkSessionLimits.SingleNodeCount or BenchmarkSessionLimits.ReplicatedNodeCount))
        {
            throw new InvalidOperationException(BenchmarkSessionProtocol.InvalidSessionSelection);
        }
    }
}
