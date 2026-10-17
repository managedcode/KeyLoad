using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class OwnerCapacityReadExecution
{
    private const long EmptyBytes = 0;
    private const int EmptyRecords = 0;

    internal static OwnerCapacitySnapshotV1 Execute(DatabaseEngine database, PrincipalRecord principal,
        OwnerCapacityReadRequestV1 query, ReplicaSiloDiscoveryState discovery,
        IOptions<DatabaseLimits> limits, TimeProvider clock,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RequireNativeLocal(query, discovery);
        GrainRequestAuthority.RequireAdministrator(principal);
        if (query.MaximumReadBytes <= EmptyBytes || query.MaximumReadBytes > database.Limits.MaxQueryReadBytes
            || query.MaximumExaminedRecords <= EmptyRecords || query.MaximumExaminedRecords > database.Limits.MaxScanRecords
            || query.MaximumResultBytes <= EmptyRecords || query.MaximumResultBytes > database.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.InvalidRequest);
        }
        var work = new ReadExecutionBudget(limits, clock, token);
        work.ConstrainLifetime(query.OriginalExpiry);
        work.ConstrainResultBytes(query.MaximumResultBytes);
        var grant = work.CreateReadGrant(query.MaximumReadBytes, query.MaximumExaminedRecords);
        OwnerCapacitySnapshotV1? complete = null;
        Exception? original = null;
        try
        {
            var value = database.ReadOwnerCapacity(principal.Id, query, work, grant, token);
            complete = value with
            {
                NativeReadBytes = grant.ReadBytes,
                NativeExaminedRecords = grant.ExaminedRecords
            };
            RequireNativeLocal(query, discovery);
            work.MeasureResult(complete);
            work.Check();
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { original = failure; }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { original = failure; }
        Exception? cleanup = null;
        try
        {
            work.CompleteSettledReadGrant(grant);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { cleanup = failure; }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { cleanup = failure; }
        NativeRequestWorkSettlement.Rethrow(NativeRequestWorkSettlement.Preserve(original, cleanup));
        return complete ?? throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
    }
    private static void RequireNativeLocal(OwnerCapacityReadRequestV1 query,
        ReplicaSiloDiscoveryState discovery)
    {
        var actual = discovery.Read();
        if (!actual.TransportReady || actual.VoterId != query.VoterId
            || actual.SiloAddress != query.SiloAddress || actual.Incarnation != query.Owner.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementCapacityProtocol.WrongCohort);
        }
    }
}
