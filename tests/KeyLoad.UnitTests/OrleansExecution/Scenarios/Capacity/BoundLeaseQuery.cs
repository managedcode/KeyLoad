using KeyLoad.Query;
using KeyLoad.Query.QueryExecution;
using K = KeyLoad.UnitTests.OrleansExecution.BoundLeaseProtocol;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class BoundLeaseQuery
{
    internal static PartitionQueryOwnedLeafRequest Create(BoundLeaseSeeded state,
        BoundLeaseOperation operation)
    {
        var database = state.Leader.Database.Database;
        var budget = operation.Budget;
        var grant = budget.CreateReadGrant(database.Limits.MaxQueryReadBytes / K.GrantDivisor,
            database.Limits.MaxScanRecords / K.GrantDivisor);
        var request = new AstQueryRequest(K.Partition,
            new(K.Collection, null, [new(K.Projection, K.ProjectionAlias)], null, [], K.RowCount), AllowFullScan: true);
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var normalized = QueryValidation.Normalize(request, database.Limits, engine.Execution);
        return database.WithPartitionQueryFenceView(K.Root, K.Partition, K.Collection, grant,
            (view, principal, resource, _) =>
            {
                var coverage = budget.CaptureResourceLifetimes(database, budget.CreateView(view, grant),
                    K.Partition, [resource]);
                var plan = PartitionQueryPlanFactory.Create(normalized, database.Store.Identity,
                    [K.Partition], database.Limits, engine.Execution).Leaves.Single();
                return new PartitionQueryOwnedLeafRequest(plan, state.PhysicalOwner, K.Partition.TenantId,
                    resource, coverage, principal);
            }, budget);
    }
}
