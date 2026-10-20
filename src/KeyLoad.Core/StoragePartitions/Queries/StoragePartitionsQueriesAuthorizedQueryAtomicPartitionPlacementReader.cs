using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes StoragePartitionsQueriesAuthorizedQueryAtomicPartitionPlacementReader operations against the caller's database context.</summary>
public static class StoragePartitionsQueriesAuthorizedQueryAtomicPartitionPlacementReader
{
    internal static AtomicPartitionPlacementResolution ReadAtomicPartitionPlacementForAuthorizedQuery(this DatabaseEngine engine, IKeyValueView view, PartitionRef partition, ReadExecutionBudgetReadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return engine.ReadPlacementWitness(view, partition);
    }

    internal static AtomicPartitionPlacementResolution ReadAtomicPartitionPlacementForAuthorizedRouting(this DatabaseEngine engine, IKeyValueView view, PartitionRef partition, ReadExecutionBudgetReadGrant grant) => engine.ReadAtomicPartitionPlacementForAuthorizedQuery(view, partition, grant);
}
