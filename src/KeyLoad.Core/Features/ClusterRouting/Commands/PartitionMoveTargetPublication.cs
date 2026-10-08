using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteMovePublication(IAtomicTransaction transaction,
        Guid commandId, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMovePublishBody>(phase.Body.Span);
        RequireMovePhaseIdentity(phase, body.Control);
        var stage = PartitionMoveTargetStorage.Read<PartitionMoveTargetStage>(transaction,
            PartitionMoveTargetStorage.Key(phase.Partition), Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        if (!stage.Installed || stage.Control.MoveId != phase.MoveId
            || body.Control.Phase != PartitionMovePhase.Published
            || body.Resources.IsDefault
            || JsonData.Fingerprint(body.Resources) != JsonData.Fingerprint(stage.Descriptor.Resources)
            || body.Control.ImageDigest != stage.Descriptor.Digest
            || body.Control.InstalledReceipt != stage.InstalledToken
            || body.Publication.MoveId != phase.MoveId || body.Publication.Installed != stage.InstalledToken
            || body.Control.PublishedPlacement is null
            || JsonData.Fingerprint(body.Control.PublishedPlacement) != JsonData.Fingerprint(body.Publication.Placement)
            || body.Publication.ControlFinalize.ControlIntentDigest != phase.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(body.Publication.ControlFinalize.PhysicalOwner, phase.ControlOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        InstallMovePublication(transaction, body.Publication, body.Control.SourcePlacement);
        RemoveRetiredMoveFence(transaction, phase.Partition, phase.MoveId);
        PartitionMoveTargetStorage.Write(transaction, PartitionMoveTargetStorage.Key(phase.Partition),
            stage with { Published = true }, Limits.MaxBatchBytes);
        return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId, position,
            phase.ControlIntentDigest), body.Control, stage.Fence, null, body.Publication.Placement);
    }

    private static bool MatchesMoveSourceRow(AtomicPartitionPlacementV1 row, AtomicPartitionPlacementResolution source)
        => row.Partition == source.Partition && row.PhysicalShardId == source.PhysicalShardId
            && row.Revision == source.Revision && row.Incarnation == source.Incarnation
            && row.PlacementEpoch == source.PlacementEpoch
            && row.VoterIds.SequenceEqual(source.VoterIds, StringComparer.Ordinal);

    private static void InstallMovePublication(IAtomicTransaction transaction,
        PartitionMovePublishedPlacement publication, AtomicPartitionPlacementResolution expectedSource)
    {
        var row = publication.Placement;
        var current = AtomicPartitionPlacementSerialization.ReadRow(transaction, row.Partition);
        if (current is not null && JsonData.Fingerprint(current) != JsonData.Fingerprint(row)
            && !MatchesMoveSourceRow(current, expectedSource))
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var directory = AtomicPartitionPlacementSerialization.ReadDirectory(transaction);
        if (directory is not null)
        { AtomicPartitionPlacementValidation.ValidateDirectory(directory); }
        var count = checked((directory?.ExplicitAssignmentCount ?? PartitionMoveProtocol.EmptyCount)
            + (current is null ? PartitionMoveProtocol.SequenceStep : PartitionMoveProtocol.EmptyCount));
        if (count > AtomicPartitionPlacementProtocol.MaximumExplicitAssignments)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        var revision = Math.Max(directory?.Revision ?? PartitionMoveProtocol.EmptyCount, row.Revision);
        PartitionMovePublishedPlacementStorage.Write(transaction, publication);
        _ = ResolveMovementPlacementOwner(transaction, row.Partition, publication.Destination, row);
        transaction.Put(AtomicPartitionPlacementSerialization.RowKey(row.Partition),
            AtomicPartitionPlacementSerialization.SerializeBounded(row));
        transaction.Put(AtomicPartitionPlacementSerialization.DirectoryKey(),
            AtomicPartitionPlacementSerialization.SerializeBounded(new AtomicPartitionPlacementDirectoryV1(
                AtomicPartitionPlacementProtocol.CurrentVersion, revision, count)));
    }
}
