namespace KeyLoad.Query.Features.Search;

internal static class FilteredVectorPlanner
{
    private const string InvalidPlan = "The filtered vector search plan is invalid.";

    internal static FilteredVectorPlan Create(int corpusCount, int requestedCount, int eligibleCount,
        int exactThreshold, int efSearch, int capacity, AnnWorkBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        budget.Charge(1);
        Validate(corpusCount, requestedCount, eligibleCount, exactThreshold, efSearch, capacity);
        var resultCount = Math.Min(requestedCount, eligibleCount);
        var useExact = eligibleCount <= exactThreshold;
        var breadth = useExact || eligibleCount == 0
            ? 0 : SelectInitialBreadth(corpusCount, resultCount, eligibleCount, efSearch, capacity);
        return new(eligibleCount, resultCount, capacity, breadth, useExact);
    }

    private static void Validate(int corpusCount, int requestedCount, int eligibleCount,
        int exactThreshold, int efSearch, int capacity)
    {
        if (corpusCount < 0 || requestedCount < 1 || eligibleCount < 0 || eligibleCount > corpusCount
            || exactThreshold < 0 || efSearch < 1 || capacity < 1 || capacity > Math.Max(corpusCount, 1))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPlan);
        }
    }

    private static int SelectInitialBreadth(int corpusCount, int resultCount, int eligibleCount,
        int efSearch, int capacity)
    {
        var numerator = checked((long)resultCount * corpusCount);
        var estimated = checked((numerator + eligibleCount - 1L) / eligibleCount);
        return checked((int)Math.Min(capacity, Math.Max((long)efSearch, estimated)));
    }
}
