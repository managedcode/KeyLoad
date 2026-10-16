using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class ResourceOwnerInventoryReadExecution
{
    internal static ResourceOwnerInventoryReadReplyV1 Execute(DatabaseEngine database, PrincipalRecord principal,
        ResourceOwnerInventoryReadRequestV1 query, ReplicaSiloDiscoveryState discovery,
        IOptions<DatabaseLimits> limits, TimeProvider clock, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        GrainRequestAuthority.RequireAdministrator(principal);
        ResourceOwnerInventoryReadShape.Require(query, principal, discovery, limits.Value, clock);
        database.VerifyPhysicalCommandOwner(query.ObservedOwner.Owner, false, token);
        var work = new ReadExecutionBudget(limits, clock, token);
        work.ConstrainLifetime(query.Request.ExpiresAt);
        work.ConstrainNativeWork(query.MaximumReadBytes, query.MaximumExaminedRecords);
        work.ConstrainResultBytes(query.MaximumResultBytes);
        var result = ResourceOwnerInventoryReadAction.Execute(database, principal.Id, query, work);
        ResourceOwnerInventoryReadShape.Require(query, principal, discovery, limits.Value, clock);
        var completed = result with
        {
            ReadBytes = work.ReadBytes,
            ExaminedRecords = work.ConstrainedNativeRecords
        };
        work.MeasureResult(completed);
        work.Check();
        return completed;
    }
}
