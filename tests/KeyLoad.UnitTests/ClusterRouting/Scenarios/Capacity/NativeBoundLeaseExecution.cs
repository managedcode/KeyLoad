using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeBoundLeaseProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeBoundLeaseExecution
{
    internal static PartitionQueryLeafResultV1 Read(NativeBoundLeaseSeeded state,
        NativeBoundLeaseOperation operation, PartitionQueryOwnedLeafRequestV2 input)
    {
        var database = state.Leader.Database.Database;
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        return PartitionQueryLifetimeLeafExecution.ExecuteBound(engine, K.Root, input, state.PhysicalOwner,
            database.EvaluationClock, operation.Expiry, operation.Parent, operation.Token);
    }
}
