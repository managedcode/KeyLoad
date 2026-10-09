using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMoveParentHeader ObserveMoveParentOutcome(IAtomicTransaction transaction,
        PrincipalRecord principal, PartitionMoveCheckpointBody body, PartitionMoveParentHeader header,
        PartitionMoveJournalReceipt receipt)
    {
        var previous = PartitionMoveParentStorage.Phase(transaction, header.Partition, header.MoveId,
            body.OriginalPhaseCommandId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireMoveParentObservation(body, header, previous);
        var presented = body.ObservedOriginalResult!;
        var actual = PartitionMoveGrantValidation.IsLocalControl(previous.Stage)
            ? RequireCheckpointLocalOutcome(transaction, principal, previous, presented) : presented;
        // Remote full native result was checked against its exact authenticated wire witness before native sealing.
        if (!PartitionMoveGrantValidation.IsLocalControl(previous.Stage) && body.OriginalOutcomeWitness is null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        var observed = previous with
        {
            OriginalResult = actual,
            ObservationCheckpointReceipt = receipt,
            OriginalOutcomeWitness = body.OriginalOutcomeWitness,
            OriginalReceiverIssuePacket = previous.OriginalGrant is { RequireReceiverIssuance: true }
                ? null : previous.OriginalReceiverIssuePacket,
            OriginalReceiverSourceWitness = previous.OriginalGrant is { RequireReceiverIssuance: true }
                ? null : previous.OriginalReceiverSourceWitness,
            OriginalPhase = RetainSettledMoveParentBody(previous) ? previous.OriginalPhase : null
        };
        if (actual.Error is null)
        {
            var value = actual.Get<PartitionMovePhaseResult>();
            RequireMoveParentResultIdentity(header, previous, value);
            observed = observed with { OriginalFence = previous.OriginalFence ?? value.Fence };
        }
        var updated = SaveMoveParentPhaseObservation(transaction, header, previous, observed, clearPending: header.PendingOriginalPhaseCommandId == previous.OriginalPhaseCommandId);
        return body.NextOriginalPhase is null ? updated
            : PromoteObservedMoveParentGrant(transaction, principal, body, previous, actual, updated, receipt);
    }

    private static void RequireMoveParentObservation(PartitionMoveCheckpointBody body,
        PartitionMoveParentHeader header, PartitionMoveParentPhase previous)
    {
        if (body.Action != PartitionMoveCheckpointAction.Observe || body.ExpectedGeneration != header.Generation
            || (header.PendingOriginalPhaseCommandId != previous.OriginalPhaseCommandId
                && header.InterruptedOriginalPhaseCommandId != previous.OriginalPhaseCommandId)
            || header.InterruptedOriginalPhaseCommandId == previous.OriginalPhaseCommandId
                && body.NextOriginalPhase is not null
            || previous.OriginalResult is not null
            || body.ObservedOriginalResult is null || body.OriginalPhase is not null || body.OriginalAuthorization is not null
            || body.OriginalReceiverSourceWitness is not null || body.OriginalReceiverIssuanceWitness is not null
            || body.OriginalReceiverIssuePacket is not null
            || body.OriginalExpiresAt != previous.OriginalExpiresAt || body.OriginalRequestNonce != previous.OriginalRequestNonce
            || body.OriginalCaptureReleaseNonce != previous.OriginalCaptureReleaseNonce
            || (body.NextOriginalPhase is null) != (body.NextOriginalPhaseCommandId is null))
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        if (body.NextOriginalPhase is null && (body.NextOriginalAuthorization is not null
            || body.NextOriginalExpiresAt != default || body.NextOriginalRequestNonce != Guid.Empty
            || body.NextOriginalCaptureReleaseNonce != Guid.Empty))
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        if (previous.OriginalGrant is { RequireReceiverIssuance: true }
            && (previous.ReceiverSourceCheckpointReceipt is null || previous.ReceiverIssuePacketCheckpointReceipt is null
                || previous.OriginalReceiverIssuanceWitness is null
                || previous.ReceiverIssuanceCheckpointReceipt is null))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        if (previous.Stage == PartitionMovePeerStage.Capture
            && (previous.OriginalCaptureWitness is null || previous.CaptureProofCheckpointReceipt is null
                || body.OriginalCaptureWitness is null || body.OriginalDescriptor is null || body.OriginalFence is null))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        if (previous.Stage != PartitionMovePeerStage.Capture && (body.OriginalCaptureWitness is not null
            || body.OriginalDescriptor is not null || body.OriginalFence is not null))
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
    }

    private static void RequireMoveParentResultIdentity(PartitionMoveParentHeader header,
        PartitionMoveParentPhase previous, PartitionMovePhaseResult actual)
    {
        if (actual.MoveId != header.MoveId || actual.Stage != previous.Stage
            || actual.Journal.CommandId != previous.OriginalPhaseCommandId || actual.Journal.AppliedPosition <= PartitionMoveProtocol.EmptyCount
            || !PhysicalOwnerEntryValidation.SameOwner(actual.Journal.PhysicalOwner, previous.OriginalReceiverOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
    private static bool RetainSettledMoveParentBody(PartitionMoveParentPhase original)
    {
        if (original.Stage == PartitionMovePeerStage.StagePage)
        { return false; }
        if (original.Stage != PartitionMovePeerStage.ControlAuthorize)
        { return true; }
        var phase = original.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var issued = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(phase.Body.Span);
        return issued.Phase.Stage != PartitionMovePeerStage.StagePage;
    }

}
