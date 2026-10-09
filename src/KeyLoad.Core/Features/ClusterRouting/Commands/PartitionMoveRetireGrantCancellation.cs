using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void DisposeRetireGrantByCancellation(IAtomicTransaction transaction, PartitionMoveParentPhase original,
        PartitionMoveRetireCancellationWitness witness)
    {
        var retained = original.OriginalGrant
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority);
        var grant = PartitionMoveGrantStorage.Read(transaction, original.Partition, retained.GrantId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (grant.RetireCancellationDisposition is not null || grant.Settlement is not null || grant.AbortDisposition is not null
            || retained.RetireCancellationDisposition is not null
            || !NativeSerialization.Serialize(grant).AsSpan().SequenceEqual(NativeSerialization.Serialize(retained)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        var disposition = CreateRetireGrantCancellationDisposition(original, witness);
        PartitionMoveGrantStorage.Write(transaction, grant with { RetireCancellationDisposition = disposition }, Limits.MaxBatchBytes);
        PartitionMoveGrantStorage.ChangeOutstanding(transaction,
            PartitionMoveGrantStorage.MoveCountKey(grant.Partition, grant.MoveId), false, Limits.MaxBatchMutations);
        PartitionMoveGrantStorage.ChangeOutstanding(transaction, grant.OperatorPrincipalId, false, Limits.MaxBatchMutations);
        PartitionMoveGrantStorage.ChangeOutstanding(transaction,
            PartitionMoveGrantStorage.DatabaseKey(grant.Partition.TenantId, grant.Partition.DatabaseId), false, Limits.MaxBatchMutations);
    }

    private static PartitionMoveRetireCancellationDisposition CreateRetireGrantCancellationDisposition(
        PartitionMoveParentPhase original, PartitionMoveRetireCancellationWitness witness)
    {
        var cancellation = witness.Cancellation;
        return new(PartitionMoveProtocol.Version, original.OriginalPhaseCommandId, original.OriginalPhaseIdentityDigest,
            original.OriginalRequestNonce, original.OriginalExpiresAt, cancellation.CancellationCommandId,
            cancellation.CancellationReceipt, cancellation.CancellationPrincipalId, cancellation.CancellationPolicyEpoch,
            original.CleanupGeneration);
    }

    private void RequireRetireGrantCancellationDisposition(IKeyValueView view, PartitionMoveControlRecord control,
        PartitionMovePhaseGrant grant)
    {
        var disposition = grant.RetireCancellationDisposition
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        var original = PartitionMoveParentStorage.Phase(view, control.Partition, control.MoveId,
            disposition.OriginalPhaseCommandId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        if (grant.Partition != control.Partition || grant.MoveId != control.MoveId)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
        var cancellation = RequireRetireGrantCancellationRecord(grant, original, disposition);
        RequireRetireGrantCancellationBody(original, grant, disposition, cancellation);
    }

    private static PartitionMoveExpiredRetireCancellation RequireRetireGrantCancellationRecord(PartitionMovePhaseGrant grant,
        PartitionMoveParentPhase original, PartitionMoveRetireCancellationDisposition disposition)
    {
        var witness = original.RetireCancellation;
        var attempt = original.RetireCancellationAttempt;
        var cancellation = witness?.Cancellation;
        if (disposition.Version != PartitionMoveProtocol.Version || grant.Stage != PartitionMovePeerStage.Retire
            || grant.Settlement is not null || grant.AbortDisposition is not null
            || original.Stage != PartitionMovePeerStage.Retire || original.OriginalPhase is null
            || original.OriginalGrant is not { RetireCancellationDisposition: null, Settlement: null, AbortDisposition: null } retained
            || original.OriginalResult is not null || original.ObservationCheckpointReceipt is null
            || original.OriginalAuthorization is not { } authorization
            || witness is null || witness.OriginalReplyBytes.IsEmpty || string.IsNullOrEmpty(witness.OriginalReplySignature)
            || attempt is null || attempt.OriginalRequestBytes.IsEmpty || string.IsNullOrEmpty(attempt.OriginalRequestSignature)
            || cancellation is null || cancellation.Version != PartitionMoveProtocol.Version
            || disposition.OriginalPhaseCommandId != original.OriginalPhaseCommandId
            || disposition.OriginalPhaseIdentityDigest != original.OriginalPhaseIdentityDigest
            || disposition.OriginalNonce != original.OriginalRequestNonce || disposition.OriginalExpiresAt != original.OriginalExpiresAt
            || disposition.CleanupGeneration != original.CleanupGeneration
            || cancellation.OriginalPhaseCommandId != original.OriginalPhaseCommandId
            || cancellation.OriginalPhaseIdentityDigest != original.OriginalPhaseIdentityDigest
            || cancellation.OriginalRequestNonce != original.OriginalRequestNonce || cancellation.OriginalExpiresAt != original.OriginalExpiresAt
            || cancellation.CleanupGeneration != original.CleanupGeneration
            || disposition.CancellationCommandId != cancellation.CancellationCommandId
            || disposition.CancellationCommandId == original.OriginalPhaseCommandId
            || attempt.CancellationCommandId != disposition.CancellationCommandId
            || disposition.ReceiverPrincipalId != cancellation.CancellationPrincipalId
            || disposition.ReceiverPolicyEpoch != cancellation.CancellationPolicyEpoch
            || string.IsNullOrEmpty(disposition.ReceiverPrincipalId) || disposition.ReceiverPolicyEpoch <= PartitionMoveProtocol.EmptyCount
            || grant.PhaseCommandId != original.OriginalPhaseCommandId || grant.ExpiresAt != original.OriginalExpiresAt
            || grant.BodyDigest != PartitionMoveParentValidation.BodyDigest(original.OriginalPhase)
            || authorization.CommandId != grant.GrantId || authorization.AppliedPosition != grant.AdmissionPosition
            || authorization.ControlIntentDigest != grant.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(authorization.PhysicalOwner, grant.ControlOwner)
            || !NativeSerialization.Serialize(grant with { RetireCancellationDisposition = null }).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(retained))
            || !NativeSerialization.Serialize(disposition.CancellationReceipt).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(cancellation.CancellationReceipt))
            || disposition.CancellationReceipt.CommandId != disposition.CancellationCommandId
            || disposition.CancellationReceipt.AppliedPosition <= PartitionMoveProtocol.EmptyCount
            || disposition.CancellationReceipt.ControlIntentDigest != grant.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(disposition.CancellationReceipt.PhysicalOwner, original.OriginalReceiverOwner))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
        return cancellation;
    }

    private static void RequireRetireGrantCancellationBody(PartitionMoveParentPhase original, PartitionMovePhaseGrant grant,
        PartitionMoveRetireCancellationDisposition disposition, PartitionMoveExpiredRetireCancellation cancellation)
    {
        var retained = original.OriginalGrant!;
        var authorization = original.OriginalAuthorization!;
        var attempt = original.RetireCancellationAttempt!;
        var cleanup = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(original.OriginalPhase!.Body.Span);
        if (cancellation.FamilyOrdinal != cleanup.FamilyOrdinal || cancellation.BatchOrdinal != original.PageOrdinal
            || cancellation.CancellationPhase.Stage != PartitionMovePeerStage.RetireCancel
            || cancellation.CancellationPhase.Partition != grant.Partition || cancellation.CancellationPhase.MoveId != grant.MoveId
            || cancellation.CancellationPhase.ControlIntentDigest != grant.ControlIntentDigest)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
        var body = NativeSerialization.Deserialize<PartitionMoveRetireCancellationBody>(cancellation.CancellationPhase.Body.Span);
        if (body.Version != PartitionMoveProtocol.Version || body.OriginalPhaseCommandId != original.OriginalPhaseCommandId
            || body.CancellationCommandId != disposition.CancellationCommandId || body.CleanupGeneration != disposition.CleanupGeneration
            || body.ActualReceiverPrincipalId != disposition.ReceiverPrincipalId || body.ActualReceiverPolicyEpoch != disposition.ReceiverPolicyEpoch
            || body.OriginalEnvelope.Nonce != disposition.OriginalNonce || body.OriginalEnvelope.ExpiresAt != disposition.OriginalExpiresAt
            || body.OriginalEnvelope.Grant is null
            || body.OriginalEnvelope.SourceDispatchWitness is not null || body.OriginalEnvelope.ReceiverIssuanceProof is not null
            || PartitionMoveOriginalDispatchIdentity.Digest(original.OriginalPhaseCommandId, body.OriginalEnvelope)
                != disposition.OriginalPhaseIdentityDigest
            || !body.OriginalRequestBytes.Span.SequenceEqual(attempt.OriginalRequestBytes.Span)
            || body.OriginalRequestSignature != attempt.OriginalRequestSignature
            || body.CancellationExpiresAt != attempt.CancellationExpiresAt
            || !NativeSerialization.Serialize(body.OriginalEnvelope.Grant).AsSpan().SequenceEqual(NativeSerialization.Serialize(retained))
            || !NativeSerialization.Serialize(body.OriginalAuthorization).AsSpan().SequenceEqual(NativeSerialization.Serialize(authorization)))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
    }
}
