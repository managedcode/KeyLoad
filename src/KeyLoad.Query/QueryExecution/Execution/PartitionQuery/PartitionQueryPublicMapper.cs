using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Query.QueryExecution;

internal static class PartitionQueryPublicMapper
{
    private const int VersionOne = 1;

    internal static PartitionQueryPage MapPublic(PartitionQueryResult result,
        ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        var rows = MapRows(result.Rows, budget);
        var leaves = MapLeaves(result.Leaves, budget);
        var page = new PartitionQueryPage(VersionOne, rows, leaves, true);
        return page;
    }

    private static ImmutableArray<PartitionQueryRow> MapRows(
        ImmutableArray<PartitionQueryCandidate> candidates, ReadExecutionBudget budget)
    {
        var rows = ImmutableArray.CreateBuilder<PartitionQueryRow>(candidates.Length);
        foreach (var candidate in candidates)
        {
            budget.Check();
            rows.Add(new(candidate.Reference, candidate.Row));
        }
        return rows.MoveToImmutable();
    }

    private static ImmutableArray<PartitionQueryLeafWitness> MapLeaves(
        ImmutableArray<PartitionQueryLeafResult> results, ReadExecutionBudget budget)
    {
        var leaves = ImmutableArray.CreateBuilder<PartitionQueryLeafWitness>(results.Length);
        foreach (var result in results)
        {
            budget.Check();
            leaves.Add(new(result.Partition, result.CutPosition, result.PolicyEpoch,
                result.SchemaVersion, result.AccessPath));
        }
        return leaves.MoveToImmutable();
    }
}
