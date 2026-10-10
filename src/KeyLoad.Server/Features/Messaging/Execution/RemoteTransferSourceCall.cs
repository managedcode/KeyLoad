using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.Messaging;

internal static class RemoteTransferSourceCall
{
    private const string NonceFormat = "N";

    internal static RemoteQueueTransferPeerCall Create(OrleansNode node, PartitionHost partition,
        NodeOptions options, GrainRequestEnvelope envelope, RemoteTransferSourceDispatch context,
        RemoteQueueTransferPeerStage stage, int maximumReplyBytes)
    {
        if (!options.MembershipAuthority.RemoteDocumentReads
            || options.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Authority
            || !PhysicalOwnerEntryValidation.Same(context.Target.DestinationOwner,
                PhysicalOwnerConfiguredTuples.Destination(options, partition)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Unavailable); }
        var discovery = node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Unavailable);
        var call = new RemoteQueueTransferPeerCall(RemoteTransferPeerProtocol.Version, envelope.RequestId,
            Guid.NewGuid().ToString(NonceFormat), envelope.ExpiresAt, partition.Configuration.LocalId,
            discovery.SiloAddress, PhysicalOwnerConfiguredTuples.Local(options, partition),
            context.Target.DestinationOwner, stage, stage == RemoteQueueTransferPeerStage.Receipt
                ? Guid.Empty : envelope.CommandId, context.Intent.PrincipalId, context.PolicyEpoch,
            context.FieldHeaderDigest, context.Intent.IntentToken, context.Claims, maximumReplyBytes, context.SourceCut);
        RemoteTransferPeerShape.Require(call);
        return call;
    }
}
