using KeyLoad.Query.Search;
using KeyLoad.UnitTests.TestInfrastructure;

namespace KeyLoad.UnitTests.Search;

internal sealed class PackedAnnDeadlineObservation
{
    internal TestElapsedClock StartedTimestamp { get; private set; } = null!;
    internal int CompletedSearches
    {
        get; private set;
    }
    internal long FailedInvocationEdgeVisits
    {
        get; private set;
    }
    internal AnnSearchResult? LastResult
    {
        get; private set;
    }
    internal long LastWorkDelta
    {
        get; private set;
    }
    internal long LastDistanceDelta
    {
        get; private set;
    }
    internal long LastEdgeDelta
    {
        get; private set;
    }
    internal bool SuccessfulDeltasMatch { get; private set; } = true;

    internal void Start(TestElapsedClock timestamp) => StartedTimestamp = timestamp;

    internal void RecordSuccess(AnnSearchResult result, AnnWorkBudget budget,
        long workBefore, long distancesBefore, long edgesBefore)
    {
        CompletedSearches++;
        LastResult = result;
        LastWorkDelta = budget.WorkUnits - workBefore;
        LastDistanceDelta = budget.DistanceEvaluations - distancesBefore;
        LastEdgeDelta = budget.EdgeVisits - edgesBefore;
        SuccessfulDeltasMatch &= result.WorkUnits == LastWorkDelta
                                 && result.DistanceEvaluations == LastDistanceDelta
                                 && result.EdgeVisits == LastEdgeDelta;
    }

    internal void RecordDeadlineFailure(long edgeDelta) => FailedInvocationEdgeVisits = edgeDelta;
}
