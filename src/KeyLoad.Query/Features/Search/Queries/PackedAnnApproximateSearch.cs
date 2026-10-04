namespace KeyLoad.Query.Features.Search;

internal readonly record struct PackedAnnApproximateResult(AnnCandidate[] Candidates, AnnSearchMode Mode);

internal static class PackedAnnApproximateSearch
{
    private const string EligibilityChanged = "The admitted ANN result count changed during candidate materialization.";
    internal static PackedAnnApproximateResult Run(PackedAnnState state, PreparedSimilarity similarity,
        ulong[]? eligibility, int limit, AnnWorkBudget budget)
    {
        var capacity = Math.Min(state.Count, 4_096);
        var buffers = new PackedAnnLayerBuffers(state.Count, capacity, true, budget);
        var current = GreedyEntry(state, similarity, budget);
        var ef = Math.Min(capacity, Math.Max(limit, state.Options.EfSearch));
        while (true)
        {
            budget.Check();
            var found = PackedAnnLayerSearch.SearchLayer(state.Graph, state.Vectors, similarity,
                current, 0, ef, state.Space.Dimension, buffers, budget);
            var eligible = CountLayerEligible(buffers, found, eligibility, budget);
            if (eligible >= limit)
            {
                return new(CopyLayerCandidates(state, buffers, found, eligibility, limit, budget), AnnSearchMode.Approximate);
            }
            if (ef == capacity)
            {
                return new(PackedAnnExactSearch.Run(state, similarity, eligibility, limit, budget),
                    AnnSearchMode.ExactAfterInsufficientCandidates);
            }
            ef = Math.Min(capacity, checked(ef * 2));
        }
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
