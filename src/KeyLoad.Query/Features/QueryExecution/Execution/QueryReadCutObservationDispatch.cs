using KeyLoad.Core;

namespace KeyLoad.Query;

internal static class QueryReadCutObservationDispatch
{
    private const long MinimumReadCut = 0;
    private const string InvalidReadCutDetail = "The completed query read cut is invalid.";
    internal static QueryReadCutObservation Capture(DatabaseEngine database,
        PrincipalRecord principal, ResourceDefinition resource, AstQueryRequest request,
        string hash, QueryPage result, ReadExecutionBudget budget)
    {
        budget.Check();
        var position = database.Store.Position;
        if (result.CutPosition < MinimumReadCut || result.CutPosition > position)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidReadCutDetail); }
        var identity = database.Store.Identity;
        return new(identity.NodeId, identity.Incarnation, identity.ReadGeneration,
            result.CutPosition, position, principal.Id, principal.PolicyEpoch, resource.SchemaVersion,
            hash, request.Partition, result.AccessPath);
    }

    internal static void Observe(IQueryReadCutObserver? observer, QueryReadCutObservation? observation,
        ReadExecutionBudget budget)
    {
        if (observer is null || observation is not { } captured)
        { return; }
        budget.Check();
        observer.Observe(captured, budget.Cancellation);
        budget.Check();
    }
}
