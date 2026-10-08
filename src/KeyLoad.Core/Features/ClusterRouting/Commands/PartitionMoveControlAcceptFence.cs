using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteMoveAcceptFence(IAtomicTransaction transaction,
        PrincipalRecord principal, Guid commandId, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveFenceAcceptBody>(phase.Body.Span);
        var current = PartitionMoveControlStorage.ReadHistory(transaction, phase.Partition,
            phase.MoveId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireMovePhaseIdentity(phase, current);
        PartitionMoveSourceFenceValidation.Require(body.Fence, phase.Partition);
        var grant = PartitionMoveGrantStorage.Read(transaction, phase.Partition, body.FenceGrantId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (body.OperatorPrincipalId != principal.Id || current.PrincipalId != principal.Id
            || current.PolicyEpoch > principal.PolicyEpoch || current.Phase != PartitionMovePhase.Prepared
            || JsonData.Fingerprint(body.Control) != JsonData.Fingerprint(current)
            || grant.Stage != PartitionMovePeerStage.Fence || grant.Settlement is null
            || grant.MoveId != current.MoveId || grant.ControlIntentDigest != phase.ControlIntentDigest
            || body.Fence.MoveId != current.MoveId || body.Fence.ControlIntentDigest != phase.ControlIntentDigest
            || grant.Settlement.CommandId != grant.PhaseCommandId
            || grant.Settlement.AppliedPosition != body.Fence.SourceCut
            || grant.ReceiverOwner.PhysicalShardId != current.SourcePlacement.PhysicalShardId
            || grant.ReceiverOwner.Incarnation != current.SourcePlacement.Incarnation
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ReceiverOwner, grant.Settlement.PhysicalOwner)
            || !PartitionMoveControlValidation.SameSource(body.Fence.SourcePlacement, current.SourcePlacement)
            || !PhysicalOwnerEntryValidation.SameOwner(body.Fence.DestinationOwner, current.DestinationOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(body.Fence.ControlOwner, phase.ControlOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var record = current with { Phase = PartitionMovePhase.Fenced, SourceCut = body.Fence.SourceCut };
        PartitionMoveControlStorage.Write(transaction, record, Limits.MaxBatchBytes);
        return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId, position,
            phase.ControlIntentDigest), record, body.Fence, null, null);
    }
}
