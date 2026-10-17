namespace KeyLoad.Query.Features.Search;

internal static class FilteredVectorPlanner
{
    private const int SingleWorkUnit = 1;
    private const int EmptyElementCount = 0;
    private const int MinimumPositiveCount = 1;
    private const int MinimumNonEmptyCapacity = 1;
    private const long AdjacentElementOffsetLong = 1L;

    private const string InvalidPlan = "The filtered vector search plan is invalid.";

    internal static FilteredVectorPlan Create(int corpusCount, int requestedCount, int eligibleCount,
        int exactThreshold, int efSearch, int capacity, AnnWorkBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        budget.Charge(SingleWorkUnit);
        Validate(corpusCount, requestedCount, eligibleCount, exactThreshold, efSearch, capacity);
        var resultCount = Math.Min(requestedCount, eligibleCount);
        var useExact = eligibleCount <= exactThreshold;
        var breadth = useExact || eligibleCount == EmptyElementCount
            ? EmptyElementCount : SelectInitialBreadth(corpusCount, resultCount, eligibleCount, efSearch, capacity);
        return new(eligibleCount, resultCount, capacity, breadth, useExact);
    }

    private static void Validate(int corpusCount, int requestedCount, int eligibleCount,
        int exactThreshold, int efSearch, int capacity)
    {
        if (corpusCount < EmptyElementCount || requestedCount < MinimumPositiveCount || eligibleCount < EmptyElementCount || eligibleCount > corpusCount
            || exactThreshold < EmptyElementCount || efSearch < MinimumPositiveCount || capacity < MinimumPositiveCount || capacity > Math.Max(corpusCount, MinimumNonEmptyCapacity))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPlan);
        }
    }

    private static int SelectInitialBreadth(int corpusCount, int resultCount, int eligibleCount,
        int efSearch, int capacity)
    {
        var numerator = checked((long)resultCount * corpusCount);
        var estimated = checked((numerator + eligibleCount - AdjacentElementOffsetLong) / eligibleCount);
        return checked((int)Math.Min(capacity, Math.Max((long)efSearch, estimated)));
    }
}
