using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal static AtomicPartitionPlacementResolution ReadAtomicPartitionPlacementForAuthorizedQuery(
        IKeyValueView view, PartitionRef partition, ReadExecutionBudgetReadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(grant);
        ValidatePartition(partition);

        var catalog = PhysicalShardCatalogRecordSerialization.Read(view, grant)
            ?? throw Errors.Fail(ErrorCode.NotFound, "The physical shard catalog is not initialized.");
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        var directory = AtomicPartitionPlacementSerialization.ReadDirectory(view, grant);
        var row = AtomicPartitionPlacementSerialization.ReadRow(view, partition, grant);
        return ResolvePlacement(partition, catalog.DefaultShard, directory, row);
    }
}
