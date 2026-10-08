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
        var (local, placement) = ReadAuthorizedQueryPlacement(view, partition, grant);
        if (placement.PhysicalShardId != local.PhysicalShardId)
        { throw Errors.Fail(ErrorCode.OwnershipLost, ForeignPlacementExecution); }
        return placement;
    }

    internal static AtomicPartitionPlacementResolution ReadAtomicPartitionPlacementForAuthorizedRouting(
        IKeyValueView view, PartitionRef partition, ReadExecutionBudgetReadGrant grant)
        => ReadAuthorizedQueryPlacement(view, partition, grant).Placement;

    private static (PhysicalShardRecord Local, AtomicPartitionPlacementResolution Placement) ReadAuthorizedQueryPlacement(
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
        var directory = AtomicPartitionPlacementSerialization.ReadDirectory(view, grant);
        var row = AtomicPartitionPlacementSerialization.ReadRow(view, partition, grant);
        var owner = row is null ? catalog.DefaultShard
            : ResolveRegisteredPlacementOwner(view, catalog.DefaultShard, row.PhysicalShardId,
                ErrorCode.Corruption, grant);
        owner = ResolveMovementPlacementOwner(view, partition, owner, row, grant);
        var placement = ResolvePlacement(partition, owner, directory, row);
        return (catalog.DefaultShard, placement);
    }
}
