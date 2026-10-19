using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using K = KeyLoad.UnitTests.Features.OrleansExecution.BoundLeaseProtocol;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

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
