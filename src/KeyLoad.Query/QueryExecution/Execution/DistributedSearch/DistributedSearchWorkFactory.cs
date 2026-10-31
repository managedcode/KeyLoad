using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query.Search;

namespace KeyLoad.Query.QueryExecution;

internal static class DistributedSearchWorkFactory
{
    private const int FirstLeaf = 0;

    internal static ImmutableArray<DistributedSearchPhaseWork> Create(DistributedSearchPhase phase,
        ImmutableArray<PartitionRef> partitions, SearchRequest search,
        ImmutableArray<DistributedTextWitness> witnesses, DistributedTextStatistics? statistics,
        GlobalBranchScope? scope, ImmutableArray<string> windows,
        ImmutableArray<GlobalBranchCandidate> selected, ReadExecutionBudget budget)
    {
        budget.Check();
        var work = ImmutableArray.CreateBuilder<DistributedSearchPhaseWork>(partitions.Length);
        for (var index = FirstLeaf; index < partitions.Length; index++)
        {
            budget.Check();
            var witness = phase == DistributedSearchPhase.Statistics ? null : witnesses[index];
            var projection = phase == DistributedSearchPhase.Projection
                ? ForPartition(selected, partitions[index], budget) : ImmutableArray<GlobalBranchCandidate>.Empty;
            work.Add(new(phase, search with
            {
                Partition = partitions[index]
            }, witness,
                statistics, scope, phase == DistributedSearchPhase.Candidates ? windows[index] : null,
                projection));
        }
        return work.MoveToImmutable();
    }

    internal static ImmutableArray<string> WindowIds(ImmutableArray<PartitionRef> partitions,
        ReadExecutionBudget budget)
    {
        var windows = ImmutableArray.CreateBuilder<string>(partitions.Length);
        foreach (var partition in partitions)
        {
            windows.Add(DistributedSearchRequestDigest.Create(partition, budget));
        }
        return windows.MoveToImmutable();
    }

    private static ImmutableArray<GlobalBranchCandidate> ForPartition(ImmutableArray<GlobalBranchCandidate> selected,
        PartitionRef partition, ReadExecutionBudget budget)
    {
        var count = FirstLeaf;
        foreach (var candidate in selected)
        {
            budget.Check();
            if (candidate.Reference.Partition == partition)
            {
                count++;
            }
        }
        var result = ImmutableArray.CreateBuilder<GlobalBranchCandidate>(count);
        foreach (var candidate in selected)
        {
            budget.Check();
            if (candidate.Reference.Partition == partition)
            {
                result.Add(candidate);
            }
        }
        return result.MoveToImmutable();
    }
}
