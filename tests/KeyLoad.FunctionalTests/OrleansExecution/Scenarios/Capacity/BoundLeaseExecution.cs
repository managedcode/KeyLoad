using KeyLoad.Query;
using KeyLoad.Query.QueryExecution;
using K = KeyLoad.FunctionalTests.OrleansExecution.BoundLeaseProtocol;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class BoundLeaseExecution
{
    internal static PartitionQueryLeafResult Read(BoundLeaseSeeded state,
        BoundLeaseOperation operation, PartitionQueryOwnedLeafRequest input)
    {
        var database = state.Leader.Database.Database;
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        return PartitionQueryLifetimeLeafExecution.ExecuteBound(engine, K.Root, input, state.PhysicalOwner,
            database.EvaluationClock, operation.Expiry, operation.Parent, operation.Token);
    }
}
