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
        if (eligibility is null)
        {
            found = ScanAll(state, similarity, candidates, found, budget);
        }
        else
        {
            found = ScanEligible(state, similarity, eligibility, candidates, found, budget);
        }
        if (found != candidates.Length)
        {
            throw new InvalidOperationException(EligibilityChanged);
        }
        return candidates;
    }

    internal static bool IsEligible(int ordinal, ulong[]? eligibility)
        => eligibility is null || (eligibility[ordinal >> 6] & (1UL << (ordinal & 63))) != 0;

    private static int ScanAll(PackedAnnState state, PreparedSimilarity similarity,
        AnnCandidate[] candidates, int found, AnnWorkBudget budget)
    {
        for (var ordinal = 0; ordinal < state.Count; ordinal++)
        {
            budget.Check();
            budget.Charge(1);
            found = ScoreOrdinal(state, similarity, ordinal, candidates, found, budget);
        }
        return found;
    }

    private static int ScanEligible(PackedAnnState state, PreparedSimilarity similarity, ulong[] eligibility,
        AnnCandidate[] candidates, int found, AnnWorkBudget budget)
    {
        for (var wordIndex = 0; wordIndex < eligibility.Length; wordIndex++)
        {
            budget.Check();
            budget.Charge(sizeof(ulong));
            var bits = eligibility[wordIndex];
            while (bits != 0)
            {
                budget.Check();
                var bit = System.Numerics.BitOperations.TrailingZeroCount(bits);
                var ordinal = checked(wordIndex * 64 + bit);
                budget.Charge(1);
                found = ScoreOrdinal(state, similarity, ordinal, candidates, found, budget);
                bits &= bits - 1;
            }
        }
        return found;
    }

    private static int ScoreOrdinal(PackedAnnState state, PreparedSimilarity similarity, int ordinal,
        AnnCandidate[] candidates, int found, AnnWorkBudget budget)
    {
        budget.ChargeDistance(state.Space.Dimension);
        var score = similarity.Score(state.Vectors.Memory(ordinal));
        return InsertTop(candidates, found,
            new(ordinal, state.Ids[ordinal], state.Revisions[ordinal], score), budget);
    }

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
