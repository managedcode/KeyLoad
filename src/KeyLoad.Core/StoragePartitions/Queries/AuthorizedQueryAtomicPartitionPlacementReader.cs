using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal AtomicPartitionPlacementResolution ReadAtomicPartitionPlacementForAuthorizedQuery(
        IKeyValueView view, PartitionRef partition, ReadExecutionBudgetReadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return ReadPlacementWitness(view, partition);
    }

    internal AtomicPartitionPlacementResolution ReadAtomicPartitionPlacementForAuthorizedRouting(
        IKeyValueView view, PartitionRef partition, ReadExecutionBudgetReadGrant grant)
        => ReadAtomicPartitionPlacementForAuthorizedQuery(view, partition, grant);
}
