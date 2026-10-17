namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Selects the exact physical voter resource names for ordinary, benchmark and functional topologies.</summary>
internal static class ClusterTopologyNodeNames
{
    private const string FirstVoterName = "node1";
    private const string SecondVoterName = "node2";
    private const string ThirdVoterName = "node3";
    private const string FunctionalEphemeralOwnershipRequired = "Functional topology requires explicit ephemeral ownership.";
    private const int SingleFunctionalNodeCount = 1;
    private const int MinimumBenchmarkNodes = 1;
    private static readonly string[] NodeNames = [FirstVoterName, SecondVoterName, ThirdVoterName];

    internal static string[] Read(int? benchmarkNodeCount, int? functionalNodeCount, bool ephemeral)
    {
        if (functionalNodeCount is not null && !ephemeral)
        {
            throw new ArgumentException(FunctionalEphemeralOwnershipRequired, nameof(functionalNodeCount));
        }
        if (functionalNodeCount is not null)
        {
            if (benchmarkNodeCount is not null
                || functionalNodeCount.Value != SingleFunctionalNodeCount && functionalNodeCount.Value != NodeNames.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(functionalNodeCount));
            }
            return [.. NodeNames.Take(functionalNodeCount.Value)];
        }
        if (benchmarkNodeCount is null)
        {
            return NodeNames;
        }
        if (benchmarkNodeCount.Value != MinimumBenchmarkNodes && benchmarkNodeCount.Value != NodeNames.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(benchmarkNodeCount));
        }
        return [.. NodeNames.Take(benchmarkNodeCount.Value)];
    }
}
