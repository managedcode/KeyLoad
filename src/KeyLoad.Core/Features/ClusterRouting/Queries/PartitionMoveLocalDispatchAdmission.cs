using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireLocalMoveDispatch(IKeyValueView view, PartitionMovePeerEnvelope original,
        PartitionMoveJournalReceipt? authorization, PartitionMoveControlRecord? control)
    {
        if (authorization is not null || original.Grant is not null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var principal = RequireMoveDispatchPrincipal(view, ResolveMoveDispatchOperator(view, original),
            EvaluationClock.GetUtcNow());
        if (original.Stage == PartitionMovePeerStage.ControlCheckpoint)
        {
            var checkpoint = NativeSerialization.Deserialize<PartitionMoveCheckpointBody>(original.Body.Span);
            if (checkpoint.Version != PartitionMoveProtocol.Version || checkpoint.OperatorPrincipalId != principal.Id
                || checkpoint.OriginalTransferRequest.MoveId != original.MoveId
                || checkpoint.OriginalTransferRequest.Partition != original.Partition
                || checkpoint.OriginalTransferRequest.Mode != PartitionMoveMode.Transfer)
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            var parent = PartitionMoveParentStorage.Header(view, original.Partition, original.MoveId, Limits.MaxBatchBytes);
            if (parent is not null)
            { PartitionMoveParentValidation.RequireReplayScope(parent, principal.Id, checkpoint.OriginalTransferRequest); }
            else if (checkpoint.Action != PartitionMoveCheckpointAction.Admit || checkpoint.ExpectedGeneration != PartitionMoveProtocol.EmptyCount)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
            return;
        }
        if (original.Stage == PartitionMovePeerStage.ControlPrepare)
        {
            var body = NativeSerialization.Deserialize<PartitionMovePrepareBody>(original.Body.Span);
            if (body.Request.MoveId != original.MoveId || body.Request.Partition != original.Partition
                || original.ControlIntentDigest != Convert.ToHexStringLower(SHA256.HashData(original.Body.Span)))
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            return;
        }
        if (control is null || control.PrincipalId != principal.Id || principal.PolicyEpoch < control.PolicyEpoch
            || PartitionMoveIntentIdentity.Digest(control) != original.ControlIntentDigest)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var phase = new PartitionMovePhaseCommand(original.Version, original.MoveId, original.Partition,
            original.ControlOwner, original.SourcePlacement, original.DestinationOwner,
            original.ControlIntentDigest, original.Stage, original.PageOrdinal, original.Body);
        RequireMovePhaseIdentity(phase, control);
    }

    private string ResolveMoveDispatchOperator(IKeyValueView view, PartitionMovePeerEnvelope original)
        => original.Stage switch
        {
            PartitionMovePeerStage.ControlCheckpoint
                => NativeSerialization.Deserialize<PartitionMoveCheckpointBody>(original.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlAdmitCommand
                => NativeSerialization.Deserialize<PartitionControlAdmitBody>(original.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlAcknowledgeCommand
                => NativeSerialization.Deserialize<PartitionControlAcknowledgeBody>(original.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlFinalizeCommand
                => NativeSerialization.Deserialize<PartitionControlFinalizeBody>(original.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlPrepare
                => NativeSerialization.Deserialize<PartitionMovePrepareBody>(original.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlAuthorize
                => NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(original.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlAdvance
                => NativeSerialization.Deserialize<PartitionMoveAdvanceBody>(original.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlAcknowledge => ResolveMoveAcknowledgmentOperator(view, original),
            PartitionMovePeerStage.ControlAcceptFence
                => NativeSerialization.Deserialize<PartitionMoveFenceAcceptBody>(original.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlFinalizeAbort or PartitionMovePeerStage.ControlCompleteRetirement
                or PartitionMovePeerStage.ControlCancelGrants
                => NativeSerialization.Deserialize<PartitionMoveCompletionBody>(original.Body.Span).OperatorPrincipalId,
            PartitionMovePeerStage.ControlFinalize or PartitionMovePeerStage.ControlBeginAbort
                => NativeSerialization.Deserialize<PartitionMoveControlBody>(original.Body.Span).OperatorPrincipalId,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMoveProtocol.Invalid)
        };

    private string ResolveMoveAcknowledgmentOperator(IKeyValueView view, PartitionMovePeerEnvelope original)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveAcknowledgeBody>(original.Body.Span);
        var grant = PartitionMoveGrantStorage.Read(view, original.Partition, body.GrantId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (grant.MoveId != original.MoveId || grant.Partition != original.Partition
            || grant.ControlIntentDigest != original.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ControlOwner, original.ControlOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        return grant.OperatorPrincipalId;
    }
}
