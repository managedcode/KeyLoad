using System.Numerics;

namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnSearch
{
    private const int BitmapRemainderMask = 63;
    private const int BitmapWordBits = 64;
    private const int MinimumNonEmptyCapacity = 1;
    private const int EmptyElementCount = 0;
    private const int NoRetainedBytes = 0;
    private const int InitialSequence = 0;
    private const int MinimumPositiveCount = 1;
    private const int FirstElementIndex = 0;
    private const int AdjacentElementOffset = 1;

    private const string InvalidQuery = "The packed ANN query, result limit or eligibility bitmap is invalid.";

    internal static AnnSearchResult Run(PackedAnnState state, ReadOnlyMemory<float> query, int limit,
        ReadOnlyMemory<ulong>? eligibility, AnnWorkBudget budget)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(budget);
        var startedWork = budget.WorkUnits;
        var startedDistances = budget.DistanceEvaluations;
        var startedEdges = budget.EdgeVisits;
        var words = checked((state.Count + BitmapRemainderMask) / BitmapWordBits);
        ValidateRequest(state, query, limit, eligibility, words);
        var initialBytes = PackedAnnReservations.InitialQueryScratch(state.Space.Dimension, words,
            eligibility is not null);
        PackedAnnReservations.RequireScratch(initialBytes, state.Options.MaxScratchBytes);
        var copiedQuery = CopyQuery(state, query, budget);
        var similarity = PrepareSimilarity(state, copiedQuery, budget);
        var copiedEligibility = CopyEligibility(state.Count, eligibility, words, budget, out var eligibleCount);
        var capacity = Math.Max(MinimumNonEmptyCapacity, Math.Min(state.Count, state.Options.MaximumSearchBreadth));
        var plan = FilteredVectorPlanner.Create(state.Count, limit, eligibleCount,
            state.Options.ExactThreshold, state.Options.EfSearch, capacity, budget);
        if (eligibleCount == EmptyElementCount)
        {
            var emptyReservation = PackedAnnReservations.ExactQueryScratch(initialBytes, NoRetainedBytes);
            PackedAnnReservations.RequireScratch(emptyReservation, state.Options.MaxScratchBytes);
            return Result([], AnnSearchMode.ExactSmallSet, emptyReservation, plan, EmptyElementCount, InitialSequence,
                budget, startedWork, startedDistances, startedEdges);
        }
        if (plan.UseExact)
        {
            return RunExact(state, similarity, copiedEligibility, plan, initialBytes, budget,
                startedWork, startedDistances, startedEdges);
        }
        return RunApproximate(state, similarity, copiedEligibility, plan, initialBytes, budget,
            startedWork, startedDistances, startedEdges);
    }

    private static AnnSearchResult RunExact(PackedAnnState state, PreparedSimilarity similarity,
        ulong[]? eligibility, FilteredVectorPlan plan, long initialBytes, AnnWorkBudget budget,
        long startedWork, long startedDistances, long startedEdges)
    {
        var reservation = PackedAnnReservations.ExactQueryScratch(initialBytes, plan.ResultCount);
        PackedAnnReservations.RequireScratch(reservation, state.Options.MaxScratchBytes);
        var candidates = PackedAnnExactSearch.Run(state, similarity, eligibility, plan.ResultCount, budget);
        return Result(candidates, AnnSearchMode.ExactSmallSet, reservation, plan, EmptyElementCount, InitialSequence,
            budget, startedWork, startedDistances, startedEdges);
    }

    private static AnnSearchResult RunApproximate(PackedAnnState state, PreparedSimilarity similarity,
        ulong[]? eligibility, FilteredVectorPlan plan, long initialBytes, AnnWorkBudget budget,
        long startedWork, long startedDistances, long startedEdges)
    {
        var visitWords = checked((state.Count + BitmapRemainderMask) / BitmapWordBits);
        var reservation = PackedAnnReservations.ApproximateQueryScratch(initialBytes, visitWords,
            plan.Capacity, plan.ResultCount);
        PackedAnnReservations.RequireScratch(reservation, state.Options.MaxScratchBytes);
        var approximate = PackedAnnApproximateSearch.Run(state, similarity, eligibility, plan, budget);
        return Result(approximate.Candidates, approximate.Mode, reservation, plan,
            approximate.ExpansionPasses, approximate.FallbackDistanceEvaluations, budget,
            startedWork, startedDistances, startedEdges);
    }

    private static void ValidateRequest(PackedAnnState state, ReadOnlyMemory<float> query, int limit,
        ReadOnlyMemory<ulong>? eligibility, int expectedBitmapWords)
    {
        if (limit < MinimumPositiveCount || limit > state.Options.MaximumSearchResults || query.Length != state.Space.Dimension
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
        var eligible = EmptyElementCount;
        for (var index = FirstElementIndex; index < bitmap.Length; index++)
        {
            budget.Check();
            var bits = bitmap[index];
            budget.Charge(sizeof(ulong));
            var remainder = count & BitmapRemainderMask;
            if (index == bitmap.Length - AdjacentElementOffset && remainder != EmptyElementCount && (bits >> remainder) != EmptyElementCount)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidQuery);
            }
            eligible = checked(eligible + BitOperations.PopCount(bits));
        }
        return eligible;
    }

    private static AnnSearchResult Result(AnnCandidate[] candidates, AnnSearchMode mode, long reservation,
        FilteredVectorPlan plan, int expansionPasses, long fallbackDistances, AnnWorkBudget budget,
        long startedWork, long startedDistances, long startedEdges)
        => new(candidates, mode, checked(budget.WorkUnits - startedWork),
            checked(budget.DistanceEvaluations - startedDistances), checked(budget.EdgeVisits - startedEdges))
        {
            ScratchBytesUpperBound = reservation,
            EligibleCount = plan.EligibleCount,
            ExpansionPasses = expansionPasses,
            FallbackDistanceEvaluations = fallbackDistances
        };
}
