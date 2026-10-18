using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string MissingPhysicalCatalog = "The physical shard catalog is not initialized.";
    private const string DifferentPhysicalOwner = "Cross-partition graph edges require one physical owner.";

    internal static void RequireSameGraphOwner(IKeyValueView view, PartitionRef source,
        PartitionRef destination, ReadExecutionBudgetReadGrant? grant = null)
    {
        var catalog = (grant is null ? PhysicalShardCatalogRecordSerialization.Read(view)
            : PhysicalShardCatalogRecordSerialization.Read(view, grant))
            ?? throw Errors.Fail(ErrorCode.NotFound, MissingPhysicalCatalog);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        var sourcePlacement = ResolvePlacement(source, catalog.DefaultShard);
        var destinationPlacement = source == destination ? sourcePlacement
            : ResolvePlacement(destination, catalog.DefaultShard);
        if (sourcePlacement.PhysicalShardId != destinationPlacement.PhysicalShardId
            || sourcePlacement.Incarnation != destinationPlacement.Incarnation
            || sourcePlacement.PlacementEpoch != destinationPlacement.PlacementEpoch
            || !sourcePlacement.VoterIds.SequenceEqual(destinationPlacement.VoterIds, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, DifferentPhysicalOwner);
        }
        if (sourcePlacement.PhysicalShardId != catalog.DefaultShard.PhysicalShardId)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ForeignPlacementExecution);
        }
    }
}
