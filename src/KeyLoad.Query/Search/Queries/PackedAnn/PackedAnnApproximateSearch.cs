namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnApproximateSearch
{
    private const int EmptyElementCount = 0;
    private const int CandidateComparisonWork = 2;
    private const int FirstElementIndex = 0;
    private const int SingleWorkUnit = 1;

    private const string EligibilityChanged = "The admitted ANN result count changed during candidate materialization.";
    private const string InsufficientCandidates = "The bounded ANN graph did not supply the requested eligible candidates.";

    internal static PackedAnnApproximateResult Run(PackedAnnState state, PreparedSimilarity similarity,
        ulong[]? eligibility, FilteredVectorPlan plan, AnnWorkBudget budget)
    {
        var buffers = new PackedAnnLayerBuffers(state.Count, plan.Capacity, true, budget);
        var current = GreedyEntry(state, similarity, budget);
        var breadth = plan.InitialBreadth;
        var expansionPasses = EmptyElementCount;
        while (true)
        {
            budget.Check();
            var found = PackedAnnLayerSearch.SearchLayer(state.Graph, state.Vectors, similarity,
                current, EmptyElementCount, breadth, state.Space.Dimension, buffers, budget);
            expansionPasses++;
            var eligible = CountLayerEligible(buffers, found, eligibility, budget);
            if (eligible >= plan.ResultCount)
            {
                var candidates = CopyLayerCandidates(state, buffers, found, eligibility,
                    plan.ResultCount, budget);
                return new(candidates, expansionPasses);
            }
            if (breadth == plan.Capacity)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, InsufficientCandidates);
            }
            breadth = Math.Min(plan.Capacity, checked(breadth * CandidateComparisonWork));
        }
    }

    private static int GreedyEntry(PackedAnnState state, PreparedSimilarity similarity, AnnWorkBudget budget)
    {
        var current = state.EntryPoint;
        for (var layer = state.MaximumLevel; layer > EmptyElementCount; layer--)
        {
            current = PackedAnnLayerSearch.Greedy(state.Graph, state.Vectors,
                similarity, current, layer, state.Space.Dimension, budget);
        }
        return current;
    }

    private static int CountLayerEligible(PackedAnnLayerBuffers buffers, int found,
        ulong[]? eligibility, AnnWorkBudget budget)
    {
        var eligible = EmptyElementCount;
        for (var index = FirstElementIndex; index < found; index++)
        {
            budget.Charge(SingleWorkUnit);
            if (PackedAnnExactSearch.IsEligible(buffers.Nodes[index], eligibility))
            {
                eligible++;
            }
        }
        return eligible;
    }

    private static AnnCandidate[] CopyLayerCandidates(PackedAnnState state, PackedAnnLayerBuffers buffers,
        int found, ulong[]? eligibility, int limit, AnnWorkBudget budget)
    {
        budget.Charge(limit);
        var candidates = new AnnCandidate[limit];
        var copied = EmptyElementCount;
        for (var index = FirstElementIndex; index < found && copied < limit; index++)
        {
            budget.Check();
            var ordinal = buffers.Nodes[index];
            budget.Charge(SingleWorkUnit);
            if (PackedAnnExactSearch.IsEligible(ordinal, eligibility))
            {
                candidates[copied++] = new(ordinal, state.Ids[ordinal], state.Revisions[ordinal], buffers.Scores[index]);
            }
        }
        if (copied != limit)
        {
            throw new InvalidOperationException(EligibilityChanged);
        }
        return candidates;
    }
}
