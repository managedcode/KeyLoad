using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMoveParentHeader ObserveMoveParentRetireCancellation(IAtomicTransaction transaction,
        PartitionMoveCheckpointBody body, PartitionMoveParentHeader header, PartitionMoveJournalReceipt receipt)
    {
        RequireMoveParentRetireCheckpointProof(transaction, body, header);
        var pending = PartitionMoveParentStorage.Phase(transaction, header.Partition, header.MoveId,
            body.OriginalPhaseCommandId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var witness = body.RetireCancellation
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority);
        var cancelled = witness.Cancellation;
        if (body.Action != PartitionMoveCheckpointAction.ObserveRetireCancellation
            || header.PendingOriginalPhaseCommandId != pending.OriginalPhaseCommandId
            || header.LastOriginalPhaseCommandId != pending.OriginalPhaseCommandId
            || pending.Stage != PartitionMovePeerStage.Retire || pending.OriginalPhase is null
            || pending.OriginalResult is not null || pending.RetireCancellation is not null
            || pending.RetireCancellationAttempt is null
            || pending.RetireCancellationAttempt.CancellationCommandId != cancelled.CancellationCommandId
            || pending.ObservationCheckpointReceipt is not null || body.ObservedOriginalResult is not null
            || body.OriginalDescriptor is not null || body.OriginalFence is not null
            || body.OriginalPhase is not null || body.OriginalAuthorization is not null
            || body.NextOriginalPhase is not null || body.NextOriginalPhaseCommandId is not null
            || body.NextOriginalAuthorization is not null || body.NextOriginalExpiresAt != default
            || body.NextOriginalRequestNonce != Guid.Empty || body.NextOriginalCaptureReleaseNonce != Guid.Empty
            || body.OriginalCaptureWitness is not null || body.OriginalOutcomeWitness is not null
            || body.OriginalReceiverIssuanceWitness is not null || body.OriginalReceiverSourceWitness is not null
            || body.OriginalReceiverIssuePacket is not null || body.OriginalExpiresAt != pending.OriginalExpiresAt
            || body.OriginalRequestNonce != pending.OriginalRequestNonce
            || body.CleanupGeneration != pending.CleanupGeneration || header.CleanupGeneration != pending.CleanupGeneration
            || cancelled.CleanupGeneration != pending.CleanupGeneration
            || cancelled.OriginalPhaseCommandId != pending.OriginalPhaseCommandId
            || cancelled.OriginalPhaseIdentityDigest != pending.OriginalPhaseIdentityDigest
            || cancelled.OriginalRequestNonce != pending.OriginalRequestNonce
            || cancelled.OriginalExpiresAt != pending.OriginalExpiresAt
            || cancelled.CancellationCommandId == pending.OriginalPhaseCommandId
            || cancelled.CancellationReceipt.CommandId != cancelled.CancellationCommandId
            || cancelled.CancellationReceipt.AppliedPosition <= PartitionMoveProtocol.EmptyCount
            || !PhysicalOwnerEntryValidation.SameOwner(cancelled.CancellationReceipt.PhysicalOwner, pending.OriginalReceiverOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        var cleanup = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(pending.OriginalPhase.Body.Span);
        if (cancelled.FamilyOrdinal != cleanup.FamilyOrdinal || cancelled.BatchOrdinal != pending.PageOrdinal)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        return SaveMoveParentRetireCancellation(transaction, header, pending, witness, receipt);
    }

    private PartitionMoveParentHeader SaveMoveParentRetireCancellation(IAtomicTransaction transaction,
        PartitionMoveParentHeader header, PartitionMoveParentPhase pending,
        PartitionMoveRetireCancellationWitness witness, PartitionMoveJournalReceipt receipt)
    {
        var retainedGrant = pending.OriginalGrant
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority);
        var disposedGrant = retainedGrant with
        { RetireCancellationDisposition = CreateRetireGrantCancellationDisposition(pending, witness) };
        var grantBytes = checked(NativeSerialization.Measure(disposedGrant) - NativeSerialization.Measure(retainedGrant));
        var observed = pending with { RetireCancellation = witness, ObservationCheckpointReceipt = receipt };
        var baseBytes = checked(header.RetainedMetadataBytes + grantBytes + NativeSerialization.Measure(observed)
            - NativeSerialization.Measure(pending) - NativeSerialization.Measure(header));
        var updated = header with
        {
            Generation = checked(header.Generation + PartitionMoveProtocol.SequenceStep),
            CleanupGeneration = checked(header.CleanupGeneration + PartitionMoveProtocol.SequenceStep),
            PendingOriginalPhaseCommandId = null
        };
        updated = MeasureMoveParentHeader(updated, baseBytes);
        if (updated.RetainedMetadataBytes > movementCheckpoints.MaxRetainedMetadataBytesPerMove)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        var bytes = checked((long)PartitionMoveGrantStorage.Key(header.Partition, retainedGrant.GrantId).Length
            + NativeSerialization.Measure(disposedGrant) + PartitionMoveParentKeys.Phase(header.Partition, header.MoveId,
            pending.OriginalPhaseCommandId).Length + NativeSerialization.Measure(observed)
            + PartitionMoveParentKeys.Header(header.Partition, header.MoveId).Length + NativeSerialization.Measure(updated));
        if (bytes > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        DisposeRetireGrantByCancellation(transaction, pending, witness);
        PartitionMoveParentStorage.WritePhase(transaction, observed, Limits.MaxBatchBytes);
        PartitionMoveParentStorage.WriteHeader(transaction, updated, Limits.MaxBatchBytes);
        return updated;
    }
    private PartitionMoveParentHeader AdmitMoveParentRetireCancellation(IAtomicTransaction transaction,
        PartitionMoveCheckpointBody body, PartitionMoveParentHeader header, PartitionMoveJournalReceipt _)
    {
        RequireMoveParentRetireCheckpointProof(transaction, body, header);
        var pending = PartitionMoveParentStorage.Phase(transaction, header.Partition, header.MoveId,
            body.OriginalPhaseCommandId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var attempt = body.RetireCancellationAttempt
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority);
        if (body.Action != PartitionMoveCheckpointAction.AdmitRetireCancellation
            || header.PendingOriginalPhaseCommandId != pending.OriginalPhaseCommandId
            || header.LastOriginalPhaseCommandId != pending.OriginalPhaseCommandId
            || pending.Stage != PartitionMovePeerStage.Retire || pending.OriginalPhase is null
            || pending.OriginalResult is not null || pending.RetireCancellation is not null
            || pending.RetireCancellationAttempt is not null || body.ObservedOriginalResult is not null
            || body.OriginalPhase is not null || body.OriginalAuthorization is not null
            || body.RetireCancellation is not null || body.OriginalDescriptor is not null || body.OriginalFence is not null
            || body.NextOriginalPhase is not null || body.NextOriginalPhaseCommandId is not null
            || body.NextOriginalAuthorization is not null || body.NextOriginalExpiresAt != default
            || body.NextOriginalRequestNonce != Guid.Empty || body.NextOriginalCaptureReleaseNonce != Guid.Empty
            || body.OriginalCaptureWitness is not null || body.OriginalOutcomeWitness is not null
            || body.OriginalReceiverIssuanceWitness is not null || body.OriginalReceiverSourceWitness is not null
            || body.OriginalReceiverIssuePacket is not null || body.OriginalExpiresAt != pending.OriginalExpiresAt
            || body.OriginalRequestNonce != pending.OriginalRequestNonce
            || body.CleanupGeneration != pending.CleanupGeneration || header.CleanupGeneration != pending.CleanupGeneration
            || attempt.Version != PartitionMoveProtocol.Version || attempt.CancellationCommandId == Guid.Empty
            || attempt.CancellationCommandId == pending.OriginalPhaseCommandId || attempt.CancellationNonce == Guid.Empty
            || attempt.CancellationNonce == pending.OriginalRequestNonce || attempt.OriginalRequestBytes.IsEmpty
            || attempt.OriginalRequestBytes.Length > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        return SaveMoveParentPhaseObservation(transaction, header, pending,
            pending with { RetireCancellationAttempt = attempt }, clearPending: false);
    }

    private void RequireMoveParentRetireCheckpointProof(IKeyValueView view, PartitionMoveCheckpointBody body,
        PartitionMoveParentHeader header)
    {
        var pending = PartitionMoveParentStorage.Phase(view, header.Partition, header.MoveId,
            body.OriginalPhaseCommandId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (body.Action is not (PartitionMoveCheckpointAction.AdmitRetireCancellation
                or PartitionMoveCheckpointAction.ObserveRetireCancellation)
            || pending.Stage != PartitionMovePeerStage.Retire || pending.OriginalPhase is null
            || pending.OriginalResult is not null || pending.RetireCancellation is not null
            || pending.ObservationCheckpointReceipt is not null
            || header.PendingOriginalPhaseCommandId != pending.OriginalPhaseCommandId
            || header.LastOriginalPhaseCommandId != pending.OriginalPhaseCommandId
            || body.OriginalExpiresAt != pending.OriginalExpiresAt || body.OriginalRequestNonce != pending.OriginalRequestNonce
            || body.CleanupGeneration != pending.CleanupGeneration || header.CleanupGeneration != pending.CleanupGeneration
            || body.OriginalCaptureReleaseNonce != pending.OriginalCaptureReleaseNonce
            || body.OriginalPhase is not null || body.OriginalAuthorization is not null || body.ObservedOriginalResult is not null
            || body.OriginalDescriptor is not null || body.OriginalFence is not null || body.OriginalCaptureWitness is not null
            || body.OriginalOutcomeWitness is not null || body.OriginalReceiverIssuanceWitness is not null
            || body.OriginalReceiverSourceWitness is not null || body.OriginalReceiverIssuePacket is not null
            || body.NextOriginalPhase is not null || body.NextOriginalPhaseCommandId is not null
            || body.NextOriginalAuthorization is not null || body.NextOriginalExpiresAt != default
            || body.NextOriginalRequestNonce != Guid.Empty || body.NextOriginalCaptureReleaseNonce != Guid.Empty
            || body.Action == PartitionMoveCheckpointAction.AdmitRetireCancellation
                && (pending.RetireCancellationAttempt is not null || body.RetireCancellationAttempt is null || body.RetireCancellation is not null)
            || body.Action == PartitionMoveCheckpointAction.ObserveRetireCancellation
                && (pending.RetireCancellationAttempt is null || body.RetireCancellationAttempt is not null || body.RetireCancellation is null))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
    }

}
