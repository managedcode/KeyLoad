using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireMoveParentInterruptedAbortAdmission(IKeyValueView view, PrincipalRecord principal,
        PartitionMoveCheckpointBody body, PartitionMoveParentHeader header, PartitionMovePhaseCommand original)
    {
        if (header.InterruptedOriginalPhaseCommandId is not null && body.OriginalTransferRequest.Mode != PartitionMoveMode.Abort)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        if (header.PendingOriginalPhaseCommandId is not { } interruptedId)
        { return; }
        if (header.InterruptedOriginalPhaseCommandId is not null || body.OriginalTransferRequest.Mode != PartitionMoveMode.Abort
            || original.Stage != PartitionMovePeerStage.ControlBeginAbort || original.PageOrdinal != PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var interrupted = PartitionMoveParentStorage.Phase(view, header.Partition, header.MoveId,
            interruptedId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (interrupted.OriginalResult is not null || interrupted.ObservationCheckpointReceipt is not null
            || interrupted.OriginalPhase is null || interruptedId == body.OriginalPhaseCommandId)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var control = PartitionMoveControlStorage.ReadHistory(view, header.Partition, header.MoveId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var abort = NativeSerialization.Deserialize<PartitionMoveControlBody>(original.Body.Span);
        if (!principal.ClusterAdministrator || control.PrincipalId != principal.Id || abort.OperatorPrincipalId != principal.Id
            || control.Phase is not (PartitionMovePhase.Prepared or PartitionMovePhase.Fenced
                or PartitionMovePhase.Captured or PartitionMovePhase.Installed)
            || original.ControlIntentDigest != PartitionMoveIntentIdentity.Digest(control)
            || !NativeSerialization.Serialize(abort.Control).AsSpan().SequenceEqual(NativeSerialization.Serialize(control)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }

    private void RequireMoveParentInterruptedTerminal(IKeyValueView view, PrincipalRecord principal, PartitionMoveParentHeader header,
        PartitionMoveControlRecord control)
    {
        if (header.InterruptedOriginalPhaseCommandId is not { } originalId)
        { return; }
        var interrupted = PartitionMoveParentStorage.Phase(view, header.Partition, header.MoveId,
            originalId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (interrupted.OriginalResult is not null && interrupted.ObservationCheckpointReceipt is not null)
        { return; }
        if (interrupted.Stage == PartitionMovePeerStage.ControlAuthorize && interrupted.OriginalGrant is null)
        {
            RequireMoveUnissuedAuthorizationAbort(view, principal, header, interrupted, control);
            return;
        }
        if (control.Phase != PartitionMovePhase.Aborted || interrupted.OriginalPhase is null
            || interrupted.OriginalGrant is not { } originalGrant)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var actual = PartitionMoveGrantStorage.Read(view, header.Partition, originalGrant.GrantId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (actual.AbortDisposition is not { } cancellation || cancellation.AppliedPosition <= PartitionMoveProtocol.EmptyCount
            || actual.PhaseCommandId != interrupted.OriginalPhaseCommandId
            || actual.ExpiresAt != interrupted.OriginalExpiresAt
            || !PhysicalOwnerEntryValidation.SameOwner(cancellation.PhysicalOwner, interrupted.OriginalReceiverOwner)
            || !NativeSerialization.Serialize(originalGrant with
            {
                Settlement = actual.Settlement,
                AbortDisposition = actual.AbortDisposition
            }).AsSpan().SequenceEqual(NativeSerialization.Serialize(actual)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        // AbortDisposition was written only by native CancelGrants after both real cleanup settlements.
        // The unknown original body and absent OriginalResult remain retained in the terminal metadata quota.
    }
}
