using System.Numerics;

namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnSearch
{
    private const string InvalidQuery = "The packed ANN query, result limit or eligibility bitmap is invalid.";

    internal static AnnSearchResult Run(PackedAnnState state, ReadOnlyMemory<float> query, int limit,
        ReadOnlyMemory<ulong>? eligibility, AnnWorkBudget budget)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(budget);
        var startedWork = budget.WorkUnits;
        var startedDistances = budget.DistanceEvaluations;
        var startedEdges = budget.EdgeVisits;
        var words = checked((state.Count + 63) / 64);
        ValidateRequest(state, query, limit, eligibility, words);
        var initialBytes = PackedAnnReservations.InitialQueryScratch(state.Space.Dimension, words,
            eligibility is not null);
        PackedAnnReservations.RequireScratch(initialBytes, state.Options.MaxScratchBytes);
        var copiedQuery = CopyQuery(state, query, budget);
        var similarity = PrepareSimilarity(state, copiedQuery, budget);
        var copiedEligibility = CopyEligibility(state.Count, eligibility, words, budget, out var eligibleCount);
        var resultCount = Math.Min(limit, eligibleCount);
        if (eligibleCount <= state.Options.ExactThreshold || state.Count == 0)
        {
            return RunExact(state, similarity, copiedEligibility, resultCount, initialBytes, budget,
                startedWork, startedDistances, startedEdges, AnnSearchMode.ExactSmallSet);
        }
        return RunApproximate(state, similarity, copiedEligibility, resultCount, initialBytes,
            budget, startedWork, startedDistances, startedEdges);
    }

    private static AnnSearchResult RunExact(PackedAnnState state, PreparedSimilarity similarity,
        ulong[]? eligibility, int resultCount, long initialBytes, AnnWorkBudget budget,
        long startedWork, long startedDistances, long startedEdges, AnnSearchMode mode)
    {
        var reservation = PackedAnnReservations.ExactQueryScratch(initialBytes, resultCount);
        PackedAnnReservations.RequireScratch(reservation, state.Options.MaxScratchBytes);
        var candidates = PackedAnnExactSearch.Run(state, similarity, eligibility, resultCount, budget);
        return Result(candidates, mode, reservation, budget, startedWork, startedDistances, startedEdges);
    }

    private static AnnSearchResult RunApproximate(PackedAnnState state, PreparedSimilarity similarity,
        ulong[]? eligibility, int resultCount, long initialBytes, AnnWorkBudget budget,
        long startedWork, long startedDistances, long startedEdges)
    {
        var capacity = Math.Min(state.Count, 4_096);
        var visitWords = checked((state.Count + 63) / 64);
        var reservation = PackedAnnReservations.ApproximateQueryScratch(initialBytes, visitWords, capacity, resultCount);
        PackedAnnReservations.RequireScratch(reservation, state.Options.MaxScratchBytes);
        var approximate = PackedAnnApproximateSearch.Run(state, similarity, eligibility,
            resultCount, budget);
        return Result(approximate.Candidates, approximate.Mode, reservation, budget,
            startedWork, startedDistances, startedEdges);
    }

    private static void ValidateRequest(PackedAnnState state, ReadOnlyMemory<float> query, int limit,
        ReadOnlyMemory<ulong>? eligibility, int expectedBitmapWords)
    {
        if (limit is < 1 or > 1_000 || query.Length != state.Space.Dimension
            || eligibility is { } bitmap && bitmap.Length != expectedBitmapWords)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidQuery);
        }
    }

    private static float[] CopyQuery(PackedAnnState state, ReadOnlyMemory<float> query, AnnWorkBudget budget)
    {
        budget.Charge(state.Space.Dimension);
        var copy = new float[state.Space.Dimension];
        query.Span.CopyTo(copy);
        return copy;
    }

    private static PreparedSimilarity PrepareSimilarity(PackedAnnState state, float[] query, AnnWorkBudget budget)
    {
        budget.Charge(state.Space.Dimension);
        return PreparedSimilarity.Create(query.AsMemory(), state.Space.Metric);
    }

    private static ulong[]? CopyEligibility(int count, ReadOnlyMemory<ulong>? eligibility, int words,
        AnnWorkBudget budget, out int eligibleCount)
    {
        if (eligibility is null)
        {
            eligibleCount = count;
            return null;
        }
        budget.Charge(checked((long)words * sizeof(ulong)));
        var source = eligibility.Value.Span;
        var copy = new ulong[words];
        source.CopyTo(copy);
        eligibleCount = CountEligible(count, copy, budget);
        return copy;
    }

    private static int CountEligible(int count, ulong[] bitmap, AnnWorkBudget budget)
    {
        var eligible = 0;
        for (var index = 0; index < bitmap.Length; index++)
        {
            budget.Check();
            var bits = bitmap[index];
            budget.Charge(sizeof(ulong));
            var remainder = count & 63;
            if (index == bitmap.Length - 1 && remainder != 0 && (bits >> remainder) != 0)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidQuery);
            }
            eligible = checked(eligible + BitOperations.PopCount(bits));
        }
        return eligible;
    }

    private static AnnSearchResult Result(AnnCandidate[] candidates, AnnSearchMode mode, long reservation,
        AnnWorkBudget budget, long startedWork, long startedDistances, long startedEdges)
        => new(candidates, mode, checked(budget.WorkUnits - startedWork),
            checked(budget.DistanceEvaluations - startedDistances), checked(budget.EdgeVisits - startedEdges))
        {
            ScratchBytesUpperBound = reservation
        };
}
