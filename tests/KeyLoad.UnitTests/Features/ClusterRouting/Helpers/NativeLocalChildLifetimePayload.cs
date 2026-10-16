using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeLocalChildLifetimePayload
{
    internal static NativeLocalChildLifetimeInput Create(NativeLocalChildLifetimeSeeded state,
        NativeLocalChildLifetimeOperation operation, int limit = K.RowCount)
    {
        var database = state.Leader.Journal.Database;
        var budget = operation.Budget;
        var grant = budget.CreateReadGrant(database.Limits.MaxQueryReadBytes / K.GrantDivisor,
            database.Limits.MaxScanRecords / K.GrantDivisor);
        var request = new AstQueryRequest(P.Partition,
            new(P.Collection, null, [new(K.Projection, K.ProjectionAlias)], null, [], limit), AllowFullScan: true);
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var normalized = QueryValidation.Normalize(request, database.Limits, engine.Execution);
        var input = database.WithPartitionQueryFenceView(P.Root, P.Partition, P.Collection, grant,
            (view, principal, resource, _) =>
            {
                var coverage = budget.CaptureResourceLifetimes(database, budget.CreateView(view, grant),
                    P.Partition, [resource]);
                var plan = PartitionQueryPlanFactory.Create(normalized, database.Store.Identity,
                    [P.Partition], database.Limits, engine.Execution).Leaves.Single();
                return new PartitionQueryOwnedLeafRequestV2(new(plan, state.PhysicalOwner, P.Partition.TenantId),
                    resource, coverage, principal);
            }, budget);
        return Sign(operation, input, Guid.NewGuid(), P.Root);
    }

    internal static NativeLocalChildLifetimeInput Sign(NativeLocalChildLifetimeOperation operation,
        PartitionQueryOwnedLeafRequestV2 input, Guid requestId, string principalId)
    {
        var bytes = NativeSerialization.Serialize(input);
        var signed = operation.Codec.CreatePartitionQueryLeafV2(requestId, principalId, bytes, operation.Expiry);
        return new(input, operation.Codec.VerifyRequest(signed, requestId));
    }
}
