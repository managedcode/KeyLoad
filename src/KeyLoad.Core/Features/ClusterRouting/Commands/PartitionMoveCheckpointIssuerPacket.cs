using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMoveParentHeader ObserveMoveParentIssuerPacket(IAtomicTransaction transaction,
        PartitionMoveCheckpointBody body, PartitionMoveParentHeader header, PartitionMoveJournalReceipt receipt)
    {
        var original = PartitionMoveParentStorage.Phase(transaction, header.Partition, header.MoveId,
            body.OriginalPhaseCommandId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireMoveParentIssuerPacket(body, header, original);
        var observed = original with
        {
            OriginalReceiverIssuePacket = body.OriginalReceiverIssuePacket,
            ReceiverIssuePacketCheckpointReceipt = receipt
        };
        return SaveMoveParentPhaseObservation(transaction, header, original, observed, clearPending: false);
    }

    private static void RequireMoveParentIssuerPacket(PartitionMoveCheckpointBody body,
        PartitionMoveParentHeader header, PartitionMoveParentPhase original)
    {
        var packet = body.OriginalReceiverIssuePacket;
        if (body.Action != PartitionMoveCheckpointAction.Observe || body.ExpectedGeneration != header.Generation
            || header.PendingOriginalPhaseCommandId != original.OriginalPhaseCommandId
            || original.OriginalResult is not null || original.OriginalPhase is null
            || original.OriginalGrant is not { RequireReceiverIssuance: true } || original.OriginalAuthorization is null
            || original.OriginalReceiverSourceWitness is null || original.ReceiverSourceCheckpointReceipt is null
            || original.OriginalReceiverIssuePacket is not null || original.ReceiverIssuePacketCheckpointReceipt is not null
            || original.OriginalReceiverIssuanceWitness is not null || original.ReceiverIssuanceCheckpointReceipt is not null
            || packet is null || packet.Version != PartitionMoveProtocol.Version
            || packet.OriginalPhaseCommandId != original.OriginalPhaseCommandId || packet.IssuanceNonce == Guid.Empty
            || packet.IssuanceNonce == original.OriginalRequestNonce || packet.OriginalRequestBytes.IsEmpty
            || body.OriginalPhase is not null || body.OriginalAuthorization is not null
            || body.ObservedOriginalResult is not null || body.OriginalDescriptor is not null || body.OriginalFence is not null
            || body.OriginalCaptureWitness is not null || body.OriginalOutcomeWitness is not null
            || body.OriginalReceiverSourceWitness is not null || body.OriginalReceiverIssuanceWitness is not null
            || body.OriginalExpiresAt != original.OriginalExpiresAt || body.OriginalRequestNonce != original.OriginalRequestNonce
            || body.OriginalCaptureReleaseNonce != original.OriginalCaptureReleaseNonce
            || body.NextOriginalPhaseCommandId is not null || body.NextOriginalPhase is not null
            || body.NextOriginalAuthorization is not null || body.NextOriginalExpiresAt != default
            || body.NextOriginalRequestNonce != Guid.Empty || body.NextOriginalCaptureReleaseNonce != Guid.Empty)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
    }
}
