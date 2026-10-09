using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMoveParentHeader ObserveMoveParentCaptureProof(IAtomicTransaction transaction,
        PartitionMoveCheckpointBody body, PartitionMoveParentHeader header, PartitionMoveJournalReceipt receipt)
    {
        var previous = PartitionMoveParentStorage.Phase(transaction, header.Partition, header.MoveId,
            body.OriginalPhaseCommandId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireMoveParentCaptureProof(body, header, previous);
        // The specialized native issuer verified the actual MAC reply against the original persisted nonce and full body.
        var retained = previous with
        {
            OriginalDescriptor = body.OriginalDescriptor,
            OriginalFence = body.OriginalFence,
            OriginalCaptureWitness = body.OriginalCaptureWitness,
            CaptureProofCheckpointReceipt = receipt
        };
        return SaveMoveParentPhaseObservation(transaction, header, previous, retained, clearPending: false);
    }

    private static void RequireMoveParentCaptureProof(PartitionMoveCheckpointBody body,
        PartitionMoveParentHeader header, PartitionMoveParentPhase previous)
    {
        if (body.Action != PartitionMoveCheckpointAction.Observe || body.ExpectedGeneration != header.Generation
            || header.PendingOriginalPhaseCommandId != previous.OriginalPhaseCommandId
            || previous.Stage != PartitionMovePeerStage.Capture || previous.OriginalPhase is null
            || previous.OriginalResult is not null || body.ObservedOriginalResult is not null
            || body.OriginalDescriptor is null || body.OriginalFence is null || body.OriginalCaptureWitness is null
            || body.OriginalPhase is not null || body.OriginalAuthorization is not null
            || body.OriginalExpiresAt != previous.OriginalExpiresAt || body.OriginalRequestNonce != previous.OriginalRequestNonce
            || body.NextOriginalPhaseCommandId is not null || body.NextOriginalPhase is not null
            || body.NextOriginalAuthorization is not null || body.NextOriginalExpiresAt != default
            || body.NextOriginalRequestNonce != Guid.Empty || body.NextOriginalCaptureReleaseNonce != Guid.Empty
            || body.OriginalCaptureReleaseNonce != previous.OriginalCaptureReleaseNonce
            || body.OriginalOutcomeWitness is not null || body.OriginalReceiverSourceWitness is not null
            || body.OriginalReceiverIssuanceWitness is not null || body.OriginalReceiverIssuePacket is not null)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        if (previous.OriginalCaptureWitness is not null || previous.CaptureProofCheckpointReceipt is not null)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
    }

    private PartitionMoveParentHeader SaveMoveParentPhaseObservation(IAtomicTransaction transaction,
        PartitionMoveParentHeader header, PartitionMoveParentPhase previous, PartitionMoveParentPhase observed,
        bool clearPending)
    {
        var phaseKey = PartitionMoveParentKeys.Phase(header.Partition, header.MoveId, observed.OriginalPhaseCommandId);
        var baseBytes = checked(header.RetainedMetadataBytes + NativeSerialization.Measure(observed)
            - NativeSerialization.Measure(previous) - NativeSerialization.Measure(header));
        var updated = header with
        {
            Generation = checked(header.Generation + PartitionMoveProtocol.SequenceStep),
            PendingOriginalPhaseCommandId = clearPending ? null : header.PendingOriginalPhaseCommandId
        };
        updated = MeasureMoveParentHeader(updated, baseBytes);
        if (updated.RetainedMetadataBytes > movementCheckpoints.MaxRetainedMetadataBytesPerMove)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        var payloadBytes = checked((long)phaseKey.Length + NativeSerialization.Measure(observed)
            + PartitionMoveParentKeys.Header(header.Partition, header.MoveId).Length + NativeSerialization.Measure(updated));
        if (payloadBytes > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        PartitionMoveParentStorage.WritePhase(transaction, observed, Limits.MaxBatchBytes);
        PartitionMoveParentStorage.WriteHeader(transaction, updated, Limits.MaxBatchBytes);
        return updated;
    }
}
