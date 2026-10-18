using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal static AtomicPartitionPlacementResolution ReadAtomicPartitionPlacementForAuthorizedQuery(
        IKeyValueView view, PartitionRef partition, ReadExecutionBudgetReadGrant grant)
        => ReadAuthorizedQueryPlacement(view, partition, grant);

    internal static AtomicPartitionPlacementResolution ReadAtomicPartitionPlacementForAuthorizedRouting(
        IKeyValueView view, PartitionRef partition, ReadExecutionBudgetReadGrant grant)
        => ReadAuthorizedQueryPlacement(view, partition, grant);

    private static AtomicPartitionPlacementResolution ReadAuthorizedQueryPlacement(
        IKeyValueView view, PartitionRef partition, ReadExecutionBudgetReadGrant grant)
    {
        const string ReadAtomicPartitionPlacementForAuthorizedQueryDetailText = "The physical shard catalog is not initialized.";

        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(grant);
        ValidatePartition(partition);

        var catalog = PhysicalShardCatalogRecordSerialization.Read(view, grant)
            ?? throw Errors.Fail(ErrorCode.NotFound, ReadAtomicPartitionPlacementForAuthorizedQueryDetailText);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        return ResolvePlacement(partition, catalog.DefaultShard);
    }
}
