using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteMoveFinalize(IAtomicTransaction transaction,
        PrincipalRecord principal, Guid commandId, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveControlBody>(phase.Body.Span);
        var current = PartitionMoveControlStorage.ReadHistory(transaction, phase.Partition,
            phase.MoveId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireMovePhaseIdentity(phase, current);
        if (body.OperatorPrincipalId != principal.Id || current.PrincipalId != principal.Id
            || current.PolicyEpoch > principal.PolicyEpoch
            || JsonData.Fingerprint(body.Control) != JsonData.Fingerprint(current)
            || phase.ControlIntentDigest != PartitionMoveIntentIdentity.Digest(current)
            || current.Phase != PartitionMovePhase.Installed || current.InstalledReceipt is null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var owners = RequireMoveDirectory(transaction);
        if (!PhysicalOwnerEntryValidation.SameOwner(owners.ControlOwner, phase.ControlOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var placement = ResolveRegisteredPlacement(transaction, phase.Partition, owners.ControlOwner);
        if (!PartitionMoveControlValidation.SameSource(placement, current.SourcePlacement))
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var journal = MoveJournalReceipt(transaction, commandId, position, phase.ControlIntentDigest);
        var row = PublishMovePlacement(transaction, current, journal);
        var published = current with { Phase = PartitionMovePhase.Published, PublishedPlacement = row };
        PartitionMoveControlStorage.Write(transaction, published, Limits.MaxBatchBytes);
        return new(phase.MoveId, phase.Stage, journal, published, null, null, row);
    }

    private static AtomicPartitionPlacementV1 PublishMovePlacement(IAtomicTransaction transaction,
        PartitionMoveControlRecord current, PartitionMoveJournalReceipt journal)
    {
        var directory = AtomicPartitionPlacementSerialization.ReadDirectory(transaction);
        if (directory is not null)
        { AtomicPartitionPlacementValidation.ValidateDirectory(directory); }
        var revision = checked((directory?.Revision ?? PartitionMoveProtocol.EmptyCount) + PartitionMoveProtocol.SequenceStep);
        var count = checked((directory?.ExplicitAssignmentCount ?? PartitionMoveProtocol.EmptyCount)
            + (current.SourcePlacement.IsFallback ? PartitionMoveProtocol.SequenceStep : PartitionMoveProtocol.EmptyCount));
        if (count > AtomicPartitionPlacementProtocol.MaximumExplicitAssignments)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        var epoch = checked(Math.Max(current.SourcePlacement.PlacementEpoch,
            current.DestinationOwner.PlacementEpoch) + PartitionMoveProtocol.SequenceStep);
        var row = new AtomicPartitionPlacementV1(AtomicPartitionPlacementProtocol.CurrentVersion,
            current.Partition, current.DestinationOwner.PhysicalShardId, revision,
            current.DestinationOwner.Incarnation, current.DestinationOwner.VoterIds, epoch);
        var lineage = new PartitionMovePublishedPlacement(PartitionMoveProtocol.Version, current.MoveId,
            row, current.SourcePlacement, current.DestinationOwner, current.InstalledReceipt!, journal);
        PartitionMovePublishedPlacementStorage.Write(transaction, lineage);
        transaction.Put(AtomicPartitionPlacementSerialization.RowKey(current.Partition),
            AtomicPartitionPlacementSerialization.SerializeBounded(row));
        transaction.Put(AtomicPartitionPlacementSerialization.DirectoryKey(),
            AtomicPartitionPlacementSerialization.SerializeBounded(new AtomicPartitionPlacementDirectoryV1(
                AtomicPartitionPlacementProtocol.CurrentVersion, revision, count)));
        return row;
    }
}
