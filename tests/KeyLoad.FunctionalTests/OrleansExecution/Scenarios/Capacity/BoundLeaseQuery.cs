using KeyLoad.Query;
using KeyLoad.Query.QueryExecution;
using K = KeyLoad.FunctionalTests.OrleansExecution.BoundLeaseProtocol;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class BoundLeaseQuery
{
    internal static PartitionQueryOwnedLeafRequest Create(BoundLeaseSeeded state,
        BoundLeaseOperation operation)
    {
        var database = state.Leader.Database.Database;
        var budget = operation.Budget;
        var grant = budget.CreateReadGrant(budget.RemainingReadGrantBytes / K.GrantDivisor, budget.RemainingReadGrantRecords / K.GrantDivisor);
        var request = new AstQueryRequest(K.Partition,
            new(K.Collection, null, [new(K.Projection, K.ProjectionAlias)], null, [], K.RowCount), AllowFullScan: true);
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var normalized = QueryValidation.Normalize(request, engine.Execution);
        return database.WithPartitionQueryFenceView(K.Root, K.Partition, K.Collection, grant,
            (view, principal, resource, _) =>
            {
                var coverage = budget.CaptureResourceLifetimes(budget.CreateView(view, grant),
                    K.Partition, [resource]);
                var plan = PartitionQueryPlanFactory.Create(normalized, database.Store.Identity,
                    [K.Partition], engine.Execution).Leaves.Single();
                return new PartitionQueryOwnedLeafRequest(plan, state.PhysicalOwner, K.Partition.TenantId,
                    resource, coverage, principal);
            }, budget);
    }
}
