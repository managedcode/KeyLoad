using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteMoveAdvance(IAtomicTransaction transaction, PrincipalRecord principal,
        Guid commandId, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveAdvanceBody>(phase.Body.Span);
        var current = PartitionMoveControlStorage.ReadHistory(transaction, phase.Partition, phase.MoveId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireMovePhaseIdentity(phase, current);
        PartitionMoveDescriptorValidation.Require(body.Descriptor, body.Fence, Limits);
        if (body.OperatorPrincipalId != principal.Id || current.PrincipalId != principal.Id
            || JsonData.Fingerprint(body.Control) != JsonData.Fingerprint(current)
            || phase.ControlIntentDigest != PartitionMoveIntentIdentity.Digest(current)
            || body.Fence.SourceCut != current.SourceCut || body.Fence.ControlIntentDigest != phase.ControlIntentDigest
            || !PartitionMoveControlValidation.SameSource(body.Fence.SourcePlacement, current.SourcePlacement)
            || !PhysicalOwnerEntryValidation.SameOwner(body.Fence.ControlOwner, phase.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(body.Fence.DestinationOwner, current.DestinationOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var updated = body.InstalledReceipt is null
            ? AdvanceCapturedMove(transaction, current, body) : AdvanceInstalledMove(transaction, current, body);
        PartitionMoveControlStorage.Write(transaction, updated, Limits.MaxBatchBytes);
        return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId, position,
            phase.ControlIntentDigest), updated, body.Fence, body.InstalledReceipt, null);
    }

    private PartitionMoveControlRecord AdvanceCapturedMove(IKeyValueView view,
        PartitionMoveControlRecord current, PartitionMoveAdvanceBody body)
    {
        if (body.SourceCaptureGrantId is not { } grantId
            || current.Phase is not (PartitionMovePhase.Fenced or PartitionMovePhase.Captured))
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var grant = PartitionMoveGrantStorage.Read(view, current.Partition, grantId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (grant.Stage != PartitionMovePeerStage.Capture || grant.Settlement is null
            || grant.MoveId != current.MoveId || grant.ControlIntentDigest != body.Fence.ControlIntentDigest
            || grant.ReceiverOwner.PhysicalShardId != current.SourcePlacement.PhysicalShardId
            || grant.ReceiverOwner.Incarnation != current.SourcePlacement.Incarnation
            || !PhysicalOwnerEntryValidation.SameOwner(grant.Settlement.PhysicalOwner, grant.ReceiverOwner)
            || grant.Settlement.CommandId != grant.PhaseCommandId
            || grant.Settlement.AppliedPosition <= body.Fence.SourceCut
            || JsonData.Fingerprint(grant.Resources) != JsonData.Fingerprint(body.Descriptor.Resources)
            || current.ImageDigest is { } digest && digest != body.Descriptor.Digest)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        return current with { Phase = PartitionMovePhase.Captured, ImageDigest = body.Descriptor.Digest };
    }

    private PartitionMoveControlRecord AdvanceInstalledMove(IKeyValueView view, PartitionMoveControlRecord current,
        PartitionMoveAdvanceBody body)
    {
        var receipt = body.InstalledReceipt
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        if (body.TargetInstallGrantId is not { } grantId)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var grant = PartitionMoveGrantStorage.Read(view, current.Partition, grantId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (grant.MoveId != current.MoveId || grant.Stage != PartitionMovePeerStage.Install
            || grant.Settlement is null || grant.AbortDisposition is not null
            || grant.PhaseCommandId != receipt.CommandId
            || grant.Settlement.CommandId != receipt.CommandId
            || grant.Settlement.AppliedPosition != receipt.Token.Position
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ReceiverOwner, current.DestinationOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(grant.Settlement.PhysicalOwner, grant.ReceiverOwner)
            || grant.PageOrdinal != body.Descriptor.Families.Sum(value => value.PageCount)
            || grant.ControlIntentDigest != body.Fence.ControlIntentDigest
            || JsonData.Fingerprint(grant.Resources) != JsonData.Fingerprint(body.Descriptor.Resources)
            || receipt.Mutations.IsDefault || !receipt.Mutations.IsEmpty)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        if (current.Phase is not (PartitionMovePhase.Captured or PartitionMovePhase.Installed)
            || current.ImageDigest != body.Descriptor.Digest || receipt.CommandId == Guid.Empty
            || receipt.Token.Incarnation != current.DestinationOwner.Incarnation
            || receipt.Token.AtomicPartitionId != current.Partition.AtomicPartitionId
            || receipt.Token.OwnershipEpoch != current.DestinationOwner.PlacementEpoch
            || receipt.Token.Position <= PartitionMoveProtocol.EmptyCount
            || current.InstalledReceipt is { } previous && previous != receipt.Token)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        return current with { Phase = PartitionMovePhase.Installed, InstalledReceipt = receipt.Token };
    }
}
