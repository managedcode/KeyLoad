using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionMoveControlValidation
{
    internal static void Require(PartitionMoveControlRecord record, PartitionRef partition)
    {
        if (record is null || record.Version != PartitionMoveProtocol.Version || record.MoveId == Guid.Empty
            || record.Partition != partition || record.SourcePlacement is null || record.DestinationOwner is null
            || string.IsNullOrWhiteSpace(record.PrincipalId) || record.PolicyEpoch <= PartitionMoveProtocol.EmptyCount
            || record.Phase == PartitionMovePhase.None || !Enum.IsDefined(record.Phase)
            || record.ControlPosition <= PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        DatabaseEngine.ValidatePartition(record.Partition);
        var source = record.SourcePlacement;
        var destination = record.DestinationOwner;
        if (source.Partition != partition || source.Incarnation == Guid.Empty || source.PhysicalShardId == Guid.Empty
            || destination.Incarnation == Guid.Empty || destination.PhysicalShardId == Guid.Empty
            || source.PhysicalShardId == destination.PhysicalShardId || source.Incarnation == destination.Incarnation
            || source.PlacementEpoch <= PartitionMoveProtocol.EmptyCount
            || destination.PlacementEpoch <= PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        RequirePhase(record);
    }

    private static void RequirePhase(PartitionMoveControlRecord record)
    {
        if (record.SourceCut < PartitionMoveProtocol.EmptyCount
            || record.Phase is not (PartitionMovePhase.Prepared or PartitionMovePhase.Aborted or PartitionMovePhase.Aborting)
                && record.SourceCut <= PartitionMoveProtocol.EmptyCount
            || record.Phase == PartitionMovePhase.Prepared && record.SourceCut != PartitionMoveProtocol.EmptyCount
            || record.Phase is PartitionMovePhase.Installed or PartitionMovePhase.Published or PartitionMovePhase.Retired
                && (record.InstalledReceipt is null || record.ImageDigest is null)
            || record.Phase is PartitionMovePhase.Published or PartitionMovePhase.Retired
                && record.PublishedPlacement is null)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        if (record.InstalledReceipt is { } installed
            && (installed.Incarnation != record.DestinationOwner.Incarnation
                || installed.AtomicPartitionId != record.Partition.AtomicPartitionId
                || installed.Position <= PartitionMoveProtocol.EmptyCount))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
    }

    internal static bool SameSource(AtomicPartitionPlacementResolution left,
        AtomicPartitionPlacementResolution right)
        => left.Partition == right.Partition && left.PhysicalShardId == right.PhysicalShardId
            && left.Incarnation == right.Incarnation && left.Revision == right.Revision
            && left.PlacementEpoch == right.PlacementEpoch
            && left.VoterIds.SequenceEqual(right.VoterIds, StringComparer.Ordinal);
}
