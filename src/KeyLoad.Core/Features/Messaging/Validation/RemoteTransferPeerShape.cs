using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferPeerShape
{
    internal static void Require(RemoteQueueTransferPeerCall call)
    {
        if (call is null || call.Version != RemoteTransferPeerProtocol.Version || call.RequestId == Guid.Empty
            || !Enum.IsDefined(call.Stage)
            || (call.Stage == RemoteQueueTransferPeerStage.Receipt
                ? call.OriginalCommandId != Guid.Empty : call.OriginalCommandId == Guid.Empty)
            || string.IsNullOrWhiteSpace(call.Nonce) || string.IsNullOrWhiteSpace(call.CallerVoter)
            || string.IsNullOrWhiteSpace(call.CallerSiloAddress) || string.IsNullOrWhiteSpace(call.IntentToken)
            || !PhysicalOwnerEntryValidation.Valid(call.SourceOwner) || !PhysicalOwnerEntryValidation.Valid(call.DestinationOwner)
            || PhysicalOwnerEntryValidation.SameOwner(call.SourceOwner.Owner, call.DestinationOwner.Owner)
            || call.LogicalPolicyEpoch < RemoteTransferPeerProtocol.MinimumPolicyEpoch || call.MaximumReplyBytes <= RemoteTransferPeerProtocol.EmptyEncodedBytes
            || call.SourceCut is null || call.IntentClaims is null || call.IntentClaims.Message is null
            || !RemoteTransferDependencyShape.Digest(call.FieldHeaderDigest))
        { throw Errors.Fail(ErrorCode.Validation, RemoteTransferPeerProtocol.Invalid); }
        JsonData.Identifier(call.LogicalPrincipalId);
        var intent = call.IntentClaims;
        if (intent.Purpose != RemoteTransferProtocol.IntentPurpose || intent.Incarnation != call.SourceOwner.Owner.Incarnation
            || intent.PrincipalId != call.LogicalPrincipalId || intent.TransferId == Guid.Empty
            || intent.Message.Queue != intent.Destination.Queue || JsonData.Fingerprint(intent.Message) != intent.Fingerprint
            || call.SourceCut.Incarnation != intent.Incarnation || call.SourceCut.AtomicPartitionId != intent.Source.Partition.AtomicPartitionId
            || call.SourceCut.Position < RemoteTransferAttemptProtocol.MinimumNativePosition)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid); }
    }

    internal static void RequireResult(RemoteQueueTransferPeerResult result, RemoteQueueTransferPeerStage expected)
    {
        if (result is null || result.Stage != expected || !Enum.IsDefined(result.Stage)
            || (expected == RemoteQueueTransferPeerStage.Receipt
                ? result.OriginalOutcome is not null : result.Receipt is not null || result.OriginalOutcome is null))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Invalid); }
    }
}
