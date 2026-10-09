using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMoveParentHeader ObserveMoveParentReceiverProof(IAtomicTransaction transaction,
        PartitionMoveCheckpointBody body, PartitionMoveParentHeader header, PartitionMoveJournalReceipt receipt)
    {
        var original = PartitionMoveParentStorage.Phase(transaction, header.Partition, header.MoveId,
            body.OriginalPhaseCommandId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireMoveParentReceiverProof(body, header, original);
        var observed = original with
        {
            OriginalReceiverIssuanceWitness = body.OriginalReceiverIssuanceWitness,
            ReceiverIssuanceCheckpointReceipt = receipt
        };
        return SaveMoveParentPhaseObservation(transaction, header, original, observed, clearPending: false);
    }

    private static void RequireMoveParentReceiverProof(PartitionMoveCheckpointBody body,
        PartitionMoveParentHeader header, PartitionMoveParentPhase original)
    {
        var witness = body.OriginalReceiverIssuanceWitness;
        if (body.Action != PartitionMoveCheckpointAction.Observe || body.ExpectedGeneration != header.Generation
            || header.PendingOriginalPhaseCommandId != original.OriginalPhaseCommandId
            || original.OriginalResult is not null || original.OriginalPhase is null
            || original.OriginalGrant is not { RequireReceiverIssuance: true } || original.OriginalAuthorization is null
            || original.OriginalReceiverSourceWitness is null || original.ReceiverSourceCheckpointReceipt is null
            || original.OriginalReceiverIssuePacket is null || original.ReceiverIssuePacketCheckpointReceipt is null
            || original.OriginalReceiverIssuanceWitness is not null || original.ReceiverIssuanceCheckpointReceipt is not null
            || witness is null || witness.Version != PartitionMoveProtocol.Version
            || witness.OriginalPhaseCommandId != original.OriginalPhaseCommandId || witness.QueryNonce == Guid.Empty
            || witness.QueryNonce == original.OriginalRequestNonce
            || body.OriginalPhase is not null || body.OriginalAuthorization is not null
            || body.ObservedOriginalResult is not null || body.OriginalDescriptor is not null || body.OriginalFence is not null
            || body.OriginalCaptureWitness is not null || body.OriginalOutcomeWitness is not null
            || body.OriginalReceiverSourceWitness is not null || body.OriginalReceiverIssuePacket is not null
            || body.OriginalExpiresAt != original.OriginalExpiresAt || body.OriginalRequestNonce != original.OriginalRequestNonce
            || body.OriginalCaptureReleaseNonce != original.OriginalCaptureReleaseNonce
            || body.NextOriginalPhaseCommandId is not null || body.NextOriginalPhase is not null
            || body.NextOriginalAuthorization is not null || body.NextOriginalExpiresAt != default
            || body.NextOriginalRequestNonce != Guid.Empty || body.NextOriginalCaptureReleaseNonce != Guid.Empty)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
    }
}
