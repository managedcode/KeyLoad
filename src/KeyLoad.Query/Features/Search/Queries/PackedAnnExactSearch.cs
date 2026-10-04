namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnExactSearch
{
    private const string EligibilityChanged = "The validated ANN eligibility count changed during exact search.";
    internal static AnnCandidate[] Run(PackedAnnState state, PreparedSimilarity similarity,
        ulong[]? eligibility, int resultCount, AnnWorkBudget budget)
    {
        budget.Charge(resultCount);
        var candidates = new AnnCandidate[resultCount];
        var found = 0;
        for (var ordinal = 0; ordinal < state.Count; ordinal++)
        {
            budget.Check();
            budget.Charge(1);
            if (!IsEligible(ordinal, eligibility))
            {
                continue;
            }
            budget.ChargeDistance(state.Space.Dimension);
            var score = similarity.Score(state.Vectors.Memory(ordinal));
            found = InsertTop(candidates, found, new(ordinal, state.Ids[ordinal], state.Revisions[ordinal], score), budget);
        }
        if (found != candidates.Length)
        {
            throw new InvalidOperationException(EligibilityChanged);
        }
        return candidates;
    }

    internal static bool IsEligible(int ordinal, ulong[]? eligibility)
        => eligibility is null || (eligibility[ordinal >> 6] & (1UL << (ordinal & 63))) != 0;

    internal static int InsertTop(AnnCandidate[] candidates, int found, AnnCandidate candidate, AnnWorkBudget budget)
    {
        var position = 0;
        while (position < found)
        {
            budget.Charge(1);
            var current = candidates[position];
            if (Better(candidate, current, budget))
            {
                break;
            }
            position++;
        }
        if (position >= candidates.Length)
        {
            return found;
        }
        var newCount = Math.Min(candidates.Length, found + 1);
        for (var index = newCount - 1; index > position; index--)
        {
            budget.Charge(1);
            candidates[index] = candidates[index - 1];
        }
        candidates[position] = candidate;
        return newCount;
    }

    private static bool Better(AnnCandidate left, AnnCandidate right, AnnWorkBudget budget)
    {
        if (left.Score != right.Score)
        {
            return left.Score > right.Score;
        }
        budget.Charge(1);
        return left.SourceOrdinal < right.SourceOrdinal;
    }
}
