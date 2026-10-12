using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Query;

internal static class WaitForIndexReadCutObservationDispatch
{
    internal static WaitForIndexReadCutObservation Capture(DatabaseEngine database,
        PrincipalRecord principal, ResourceDefinition resource, WaitForIndexRequest request,
        WaitForIndexResult result, ReadExecutionBudget budget)
    {
        budget.Check();
        var identity = database.Store.Identity;
        var position = database.Store.Position;
        var digest = Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(request)));
        budget.Check();
        return new(identity.NodeId, identity.Incarnation, identity.ReadGeneration, position,
            result.AppliedToken, principal.Id, principal.PolicyEpoch, resource.SchemaVersion,
            digest, request.Partition);
    }

    internal static void Observe(IQueryReadCutObserver? observer, WaitForIndexReadCutObservation? observation,
        ReadExecutionBudget budget)
    {
        if (observer is null || observation is not { } captured)
        { return; }
        budget.Check();
        observer.Observe(captured, budget.Cancellation);
        budget.Check();
    }
}
