using KeyLoad.Core.Features.Messaging;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Messaging;

internal static class RemoteTransferPeerReplyValidation
{
    internal static void Require(RemoteQueueTransferPeerCall call, RemoteDocumentReplyV1 reply,
        NodeOptions options, IOptions<OrleansMembershipOptions> membership, TimeProvider clock, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var owner = call.DestinationOwner.Owner;
        if (call.ExpiresAt <= clock.GetUtcNow() || reply.RequestId != call.RequestId || reply.Nonce != call.Nonce
            || reply.Result is not null || reply.QueryLeaf is not null || reply.SearchLeaf is not null
            || reply.Controlled is not null || reply.ControlledBlob is not null
            || (reply.Error is null) != (reply.QueueTransfer is not null)
            || reply.Error is null && reply.SafeDetail is not null
            || reply.Error is { } error && (!Enum.IsDefined(error) || string.IsNullOrWhiteSpace(reply.SafeDetail))
            || reply.EndpointDiscovery is not { TransportReady: true } discovery
            || discovery.VoterId != owner.VoterIds[RemoteTransferPeerProtocol.SelectedVoter]
            || discovery.Incarnation != owner.Incarnation || discovery.ClusterId != options.ClusterId
            || discovery.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || discovery.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || discovery.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal
            || !ReplicaMembershipAuthorityValidation.CanonicalAddress(discovery.SiloAddress, membership)
            || SiloAddress.FromParsableString(discovery.SiloAddress).Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteTransferPeerProtocol.Invalid); }
        if (reply.QueueTransfer is { } result)
        { RemoteTransferPeerShape.RequireResult(result, call.Stage); }
    }
}
