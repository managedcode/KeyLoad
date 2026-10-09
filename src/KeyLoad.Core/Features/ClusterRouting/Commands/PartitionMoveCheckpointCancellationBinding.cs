using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void RequireMoveParentCancellationBinding(PartitionMoveParentHeader header,
        PartitionMoveParentPhase original, PartitionMoveParentCancellation cancellation)
    {
        var phase = cancellation.CancellationPhase;
        var receipt = cancellation.CancellationReceipt;
        if (cancellation.Version != PartitionMoveProtocol.Version || cancellation.CancellationPolicyEpoch <= PartitionMoveProtocol.EmptyCount
            || cancellation.OriginalPhaseCommandId != original.OriginalPhaseCommandId
            || receipt.CommandId == Guid.Empty || receipt.CommandId == original.OriginalPhaseCommandId
            || receipt.CommandId == header.MoveId || receipt.AppliedPosition <= PartitionMoveProtocol.EmptyCount
            || phase.Version != PartitionMoveProtocol.Version || phase.Stage != PartitionMovePeerStage.ControlCheckpoint
            || phase.MoveId != header.MoveId || phase.Partition != header.Partition || phase.PageOrdinal != PartitionMoveProtocol.EmptyCount
            || phase.GrantId is not null || phase.Resources.IsDefault || !phase.Resources.IsEmpty
            || original.OriginalPhase is null || phase.ControlIntentDigest != original.OriginalPhase.ControlIntentDigest
            || receipt.ControlIntentDigest != phase.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(receipt.PhysicalOwner, header.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(phase.ControlOwner, header.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(phase.DestinationOwner, header.DestinationOwner)
            || !NativeSerialization.Serialize(phase.SourcePlacement).AsSpan().SequenceEqual(NativeSerialization.Serialize(header.SourcePlacement)))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
        var body = NativeSerialization.Deserialize<PartitionMoveCheckpointBody>(phase.Body.Span);
        PartitionMoveParentValidation.RequireReplayScope(header, header.OperatorPrincipalId, body.OriginalTransferRequest);
        if (body.Action != PartitionMoveCheckpointAction.CancelUnprepared
            || body.OperatorPrincipalId != header.OperatorPrincipalId || body.OriginalTransferRequest.Mode != PartitionMoveMode.Abort
            || body.ExpectedGeneration <= PartitionMoveProtocol.EmptyCount || body.ExpectedGeneration >= header.Generation
            || body.OriginalPhaseCommandId != original.OriginalPhaseCommandId || body.ObservedOriginalResult is not null)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
        RequireMoveParentCancellationOriginal(body, original);
    }

    private void RequireMovePrepareNotCancelled(IKeyValueView view, PrincipalRecord principal,
        Guid commandId, PartitionMovePhaseCommand incoming)
    {
        var header = PartitionMoveParentStorage.Header(view, incoming.Partition, incoming.MoveId, Limits.MaxBatchBytes);
        var requested = NativeSerialization.Deserialize<PartitionMovePrepareBody>(incoming.Body.Span);
        if (header is null)
        {
            if (requested.RequireParentCheckpoint)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
            return;
        }
        PartitionMoveParentValidation.RequireReplayScope(header, principal.Id,
            requested.Request);
        if (header.ControlOwner.Incarnation != Store.Identity.Incarnation
            || !PhysicalOwnerEntryValidation.SameOwner(incoming.ControlOwner, header.ControlOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var first = PartitionMoveParentStorage.Phase(view, header.Partition, header.MoveId,
            header.InterruptedOriginalPhaseCommandId ?? header.PendingOriginalPhaseCommandId ?? header.LastOriginalPhaseCommandId,
            Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (requested.RequireParentCheckpoint && (first.Stage != PartitionMovePeerStage.ControlPrepare
            || first.OriginalPhase is null || first.OriginalIssuancePolicyEpoch != principal.PolicyEpoch
            || commandId != first.OriginalPhaseCommandId
            || !NativeSerialization.Serialize(incoming).AsSpan().SequenceEqual(NativeSerialization.Serialize(first.OriginalPhase))))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        if (requested.RequireParentCheckpoint)
        { RequireMoveParentOriginalIssuance(view, principal, first); }
        if (first.Cancellation is not { } cancellation)
        { return; }
        RequireMoveParentCancellationBinding(header, first, cancellation);
        if (commandId != first.OriginalPhaseCommandId || first.OriginalPhase is null
            || !NativeSerialization.Serialize(incoming).AsSpan().SequenceEqual(NativeSerialization.Serialize(first.OriginalPhase)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict);
    }
}
