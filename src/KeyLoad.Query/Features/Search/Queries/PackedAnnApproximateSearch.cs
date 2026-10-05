namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnApproximateSearch
{
    private const string EligibilityChanged = "The admitted ANN result count changed during candidate materialization.";

    internal static PackedAnnApproximateResult Run(PackedAnnState state, PreparedSimilarity similarity,
        ulong[]? eligibility, FilteredVectorPlan plan, AnnWorkBudget budget)
    {
        var buffers = new PackedAnnLayerBuffers(state.Count, plan.Capacity, true, budget);
        var current = GreedyEntry(state, similarity, budget);
        var breadth = plan.InitialBreadth;
        var expansionPasses = 0;
        while (true)
        {
            budget.Check();
            var found = PackedAnnLayerSearch.SearchLayer(state.Graph, state.Vectors, similarity,
                current, 0, breadth, state.Space.Dimension, buffers, budget);
            expansionPasses++;
            var eligible = CountLayerEligible(buffers, found, eligibility, budget);
            if (eligible >= plan.ResultCount)
            {
                var candidates = CopyLayerCandidates(state, buffers, found, eligibility,
                    plan.ResultCount, budget);
                return new(candidates, AnnSearchMode.Approximate, expansionPasses, 0);
            }
            if (breadth == plan.Capacity)
            {
                return RunExactFallback(state, similarity, eligibility, plan, expansionPasses, budget);
            }
            breadth = Math.Min(plan.Capacity, checked(breadth * 2));
        }
    }

    private static PackedAnnApproximateResult RunExactFallback(PackedAnnState state,
        PreparedSimilarity similarity, ulong[]? eligibility, FilteredVectorPlan plan,
        int expansionPasses, AnnWorkBudget budget)
    {
        var startedDistances = budget.DistanceEvaluations;
        var candidates = PackedAnnExactSearch.Run(state, similarity, eligibility, plan.ResultCount, budget);
        var fallbackDistances = checked(budget.DistanceEvaluations - startedDistances);
        return new(candidates, AnnSearchMode.ExactAfterInsufficientCandidates,
            expansionPasses, fallbackDistances);
    }

    private static int GreedyEntry(PackedAnnState state, PreparedSimilarity similarity, AnnWorkBudget budget)
    {
        var current = state.EntryPoint;
        for (var layer = state.MaximumLevel; layer > 0; layer--)
        {
            current = PackedAnnLayerSearch.Greedy(state.Graph, state.Vectors,
                similarity, current, layer, state.Space.Dimension, budget);
        }
        return current;
    }

    private static int CountLayerEligible(PackedAnnLayerBuffers buffers, int found,
        ulong[]? eligibility, AnnWorkBudget budget)
    {
        var eligible = 0;
        for (var index = 0; index < found; index++)
        {
            budget.Charge(1);
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
        var copied = 0;
        for (var index = 0; index < found && copied < limit; index++)
        {
            budget.Check();
            var ordinal = buffers.Nodes[index];
            budget.Charge(1);
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
