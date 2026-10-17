using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

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
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteTransferPeerProtocol.Invalid);
        }
        RequireSourceLifetime(call);
        JsonData.Identifier(call.LogicalPrincipalId);
        var intent = call.IntentClaims;
        if (intent.Purpose != RemoteTransferProtocol.IntentPurpose || intent.Incarnation != call.SourceOwner.Owner.Incarnation
            || intent.PrincipalId != call.LogicalPrincipalId || intent.TransferId == Guid.Empty
            || intent.Message.Queue != intent.Destination.Queue || JsonData.Fingerprint(intent.Message) != intent.Fingerprint
            || call.SourceCut.Incarnation != intent.Incarnation || call.SourceCut.AtomicPartitionId != intent.Source.Partition.AtomicPartitionId
            || call.SourceCut.Position < RemoteTransferAttemptProtocol.MinimumNativePosition)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid);
        }
    }

    private static void RequireSourceLifetime(RemoteQueueTransferPeerCall call)
    {
        if (call.SourceDefinition is null || call.SourceLifetime is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid);
        }
        var source = call.IntentClaims.Source;
        if (source is null || source.Partition is null)
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteTransferPeerProtocol.Invalid);
        }
        DatabaseEngine.ValidatePartition(source.Partition);
        ResourceLifetimeIdentity.Validate(call.SourceLifetime, source.Partition.TenantId,
            source.Partition.DatabaseId, call.SourceDefinition);
        if (call.SourceDefinition.Name != source.Queue || call.SourceDefinition.Kind != ResourceKind.WorkQueue
            || call.SourceDefinition.TransactionDomainId != source.Partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
        }
    }

    internal static void RequireResult(RemoteQueueTransferPeerResult result, RemoteQueueTransferPeerStage expected)
    {
        if (result is null || result.Stage != expected || !Enum.IsDefined(result.Stage)
            || (expected == RemoteQueueTransferPeerStage.Receipt
                ? result.OriginalOutcome is not null : result.Receipt is not null || result.OriginalOutcome is null))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Invalid);
        }
    }
}
