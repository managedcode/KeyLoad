using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private RemoteTransferNativeProof? ReadRemoteTransferNativeProof(ReplicatedOperation operation)
    {
        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        if (payload.TransferProof.IsEmpty)
        { return null; }
        RequireNativeBudget(payload.TransferProof.Length);
        var proof = NativeSerialization.Deserialize<RemoteTransferNativeProof>(payload.TransferProof.Span);
        RemoteTransferPeerShape.Require(proof.Call);
        RequireRemoteTransferPeerOwner().Require(proof);
        var request = Payload<CommandRequest>(operation);
        if (operation.Kind != OperationKind.Batch || operation.Id != proof.Call.OriginalCommandId
            || operation.PrincipalId != proof.TechnicalPrincipalId
            || operation.EvaluatedAt != proof.OriginalEvaluatedAt
            || operation.EvaluatedAt >= proof.Call.ExpiresAt
            || proof.Call.Stage != RemoteQueueTransferPeerStage.Accept
            || request.CommandId != operation.Id || request.Partition != proof.Call.IntentClaims.Destination.Partition
            || request.Mutations.Length != RemoteTransferPeerProtocol.SingleMutation
            || request.Mutations[RemoteTransferPeerProtocol.FirstMutation] is not AcceptQueueTransfer accept
            || accept.DestinationQueue != proof.Call.IntentClaims.Destination
            || accept.IntentToken != proof.Call.IntentToken)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid); }
        return proof;
    }

    private global::KeyLoad.AtomicPartitionPlacementResolution AuthorizeRemoteTransferNativeProof(IKeyValueView view, PrincipalRecord principal,
        ReplicatedOperation operation, RemoteTransferNativeProof proof)
    {
        RequireRemoteTransferPeerAtView(view, principal, proof);
        var request = Payload<CommandRequest>(operation);
        var placement = ReadPlacementWitness(view, request.Partition);
        if (request.OwnershipEpoch != placement.PlacementEpoch)
        { throw Errors.Fail(ErrorCode.OwnershipLost, StalePartitionOwnershipMessage); }
        return placement;
    }
}
