namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnExactSearch
{
    private const int EmptyElementCount = 0;
    private const int BitmapWordShift = 6;
    private const ulong LowestBitmapBit = 1UL;
    private const int BitmapRemainderMask = 63;
    private const int FirstElementIndex = 0;
    private const int SingleWorkUnit = 1;
    private const int BitmapWordBits = 64;
    private const int AdjacentElementOffset = 1;

    private const string EligibilityChanged = "The validated ANN eligibility count changed during exact search.";
    internal static AnnCandidate[] Run(PackedAnnState state, PreparedSimilarity similarity,
        ulong[]? eligibility, int resultCount, AnnWorkBudget budget)
    {
        budget.Charge(resultCount);
        var candidates = new AnnCandidate[resultCount];
        var found = EmptyElementCount;
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
        => eligibility is null || (eligibility[ordinal >> BitmapWordShift] & (LowestBitmapBit << (ordinal & BitmapRemainderMask))) != EmptyElementCount;

    private static int ScanAll(PackedAnnState state, PreparedSimilarity similarity,
        AnnCandidate[] candidates, int found, AnnWorkBudget budget)
    {
        for (var ordinal = FirstElementIndex; ordinal < state.Count; ordinal++)
        {
            budget.Charge(SingleWorkUnit);
            found = ScoreOrdinal(state, similarity, ordinal, candidates, found, budget);
        }
        return found;
    }

    private static int ScanEligible(PackedAnnState state, PreparedSimilarity similarity, ulong[] eligibility,
        AnnCandidate[] candidates, int found, AnnWorkBudget budget)
    {
        for (var wordIndex = FirstElementIndex; wordIndex < eligibility.Length; wordIndex++)
        {
            budget.Charge(sizeof(ulong));
            var bits = eligibility[wordIndex];
            while (bits != EmptyElementCount)
            {
                budget.Check();
                var bit = System.Numerics.BitOperations.TrailingZeroCount(bits);
                var ordinal = checked(wordIndex * BitmapWordBits + bit);
                budget.Charge(SingleWorkUnit);
                found = ScoreOrdinal(state, similarity, ordinal, candidates, found, budget);
                bits &= bits - AdjacentElementOffset;
            }
        }
        return found;
    }

    private static int ScoreOrdinal(PackedAnnState state, PreparedSimilarity similarity, int ordinal,
        AnnCandidate[] candidates, int found, AnnWorkBudget budget)
    {
        budget.ChargeDistance(state.Space.Dimension);
        var score = similarity.ScorePacked(state.Vectors, ordinal);
        return InsertTop(candidates, found,
            new(ordinal, state.Ids[ordinal], state.Revisions[ordinal], score), budget);
    }

    internal static int InsertTop(AnnCandidate[] candidates, int found, AnnCandidate candidate, AnnWorkBudget budget)
    {
        var position = FirstElementIndex;
        while (position < found)
        {
            budget.Charge(SingleWorkUnit);
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
        var newCount = Math.Min(candidates.Length, found + AdjacentElementOffset);
        for (var index = newCount - AdjacentElementOffset; index > position; index--)
        {
            budget.Charge(SingleWorkUnit);
            candidates[index] = candidates[index - AdjacentElementOffset];
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
        budget.Charge(SingleWorkUnit);
        return left.SourceOrdinal < right.SourceOrdinal;
    }
}
