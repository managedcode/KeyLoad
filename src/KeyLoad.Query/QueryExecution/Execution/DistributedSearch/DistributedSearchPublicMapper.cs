using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query.Search;

namespace KeyLoad.Query.QueryExecution;

internal static class DistributedSearchPublicMapper
{
    private const int VersionOne = 1;
    private const int NoMatches = 0;
    private const int SingleMatch = 1;
    private const string AccessPath = "distributedCanonicalSearch";
    private const string InvalidProjection = "The distributed search projection does not match its original global selection.";

    internal static DistributedSearchPage Map(ImmutableArray<GlobalBranchCandidate> selected,
        ImmutableArray<DistributedSearchLeafResult> projected,
        ImmutableArray<DistributedTextWitness> witnesses, string epoch,
        SearchRankFusion fusion, bool explain, ReadExecutionBudget budget)
    {
        budget.Check();
        var hits = ImmutableArray.CreateBuilder<RankedDocument>(selected.Length);
        foreach (var candidate in selected)
        {
            budget.Check();
            var hit = Find(candidate, projected, budget);
            hits.Add(hit with
            {
                Explanation = explain ? fusion.Explain(candidate.Reference) : null
            });
        }
        var leaves = ImmutableArray.CreateBuilder<PartitionQueryLeafWitness>(witnesses.Length);
        foreach (var witness in witnesses)
        {
            budget.Check();
            leaves.Add(new(witness.Partition, witness.CutPosition, witness.PolicyEpoch, witness.SchemaVersion, AccessPath));
        }
        var page = new DistributedSearchPage(VersionOne, hits.MoveToImmutable(), leaves.MoveToImmutable(), epoch, true);
        return page;
    }

    private static RankedDocument Find(GlobalBranchCandidate candidate,
        ImmutableArray<DistributedSearchLeafResult> leaves, ReadExecutionBudget budget)
    {
        RankedDocument? selected = null;
        var matches = NoMatches;
        foreach (var leaf in leaves)
        {
            if (leaf.Projection is not { } projection)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidProjection);
            }
            foreach (var hit in projection.Hits)
            {
                budget.Check();
                if (hit.Document.Reference != candidate.Reference)
                {
                    continue;
                }
                if (hit.Document.Revision != candidate.Revision
                    || BitConverter.DoubleToInt64Bits(hit.Score) != BitConverter.DoubleToInt64Bits(candidate.Score))
                {
                    throw Errors.Fail(ErrorCode.Corruption, InvalidProjection);
                }
                selected = hit;
                matches++;
            }
        }
        return matches == SingleMatch && selected is not null ? selected
            : throw Errors.Fail(ErrorCode.Corruption, InvalidProjection);
    }
}
