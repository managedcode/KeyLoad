using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static PhysicalShardRecord ResolveMovementPlacementOwner(IKeyValueView view,
        PartitionRef partition, PhysicalShardRecord registered, AtomicPartitionPlacementV1? row,
        ReadExecutionBudgetReadGrant? grant = null)
    {
        if (row is null || row.PlacementEpoch == registered.PlacementEpoch)
        { return registered; }
        var key = PartitionMovePublishedPlacementStorage.Key(partition);
        var lineage = grant is null
            ? AtomicPartitionPlacementSerialization.Read<PartitionMovePublishedPlacement>(view, key)
            : AtomicPartitionPlacementSerialization.Read<PartitionMovePublishedPlacement>(view, key, grant);
        if (lineage is null || lineage.Version != PartitionMoveProtocol.Version || lineage.MoveId == Guid.Empty
            || lineage.Source is null || lineage.Placement is null || lineage.Destination is null
            || lineage.Installed is null || lineage.ControlFinalize is null
            || lineage.ControlFinalize.PhysicalOwner is null
            || lineage.Source.Incarnation == Guid.Empty || lineage.Source.PhysicalShardId == Guid.Empty
            || lineage.Source.PlacementEpoch <= PartitionMoveProtocol.EmptyCount
            || lineage.Source.VoterIds.IsDefault || registered.VoterIds.IsDefault
            || lineage.Source.Incarnation == registered.Incarnation
            || lineage.Source.PhysicalShardId == registered.PhysicalShardId
            || !Features.ClusterRouting.Validation.PartitionMoveSourceFenceValidation.ValidDigest(lineage.ControlFinalize.ControlIntentDigest)
            || lineage.Source.Partition != partition || lineage.Placement.Partition != partition
            || JsonData.Fingerprint(lineage.Placement) != JsonData.Fingerprint(row)
            || !Features.ClusterRouting.Validation.PhysicalOwnerEntryValidation.SameOwner(lineage.Destination, registered)
            || lineage.Installed.Incarnation != registered.Incarnation
            || lineage.Installed.AtomicPartitionId != partition.AtomicPartitionId
            || lineage.Installed.OwnershipEpoch != registered.PlacementEpoch
            || lineage.Installed.Position <= PartitionMoveProtocol.EmptyCount
            || lineage.ControlFinalize.CommandId == Guid.Empty
            || lineage.ControlFinalize.AppliedPosition <= PartitionMoveProtocol.EmptyCount
            || row.PlacementEpoch != checked(Math.Max(lineage.Source.PlacementEpoch,
                registered.PlacementEpoch) + PartitionMoveProtocol.SequenceStep))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        return registered with { PlacementEpoch = row.PlacementEpoch };
    }
}
