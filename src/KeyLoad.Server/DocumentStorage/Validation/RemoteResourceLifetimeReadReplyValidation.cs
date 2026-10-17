using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteResourceLifetimeReadReplyValidation
{
    internal static void Require(RemoteResourceLifetimeReadCallV2 call, RemoteResourceLifetimeReadReplyV2 reply,
        NodeOptions options, IOptions<OrleansMembershipOptions> membership, TimeProvider clock, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var owner = call.Destination.Owner;
        if (call.ExpiresAt <= clock.GetUtcNow() || reply.Version != RemoteResourceLifetimeReadProtocol.Version
            || reply.Kind != call.Kind || reply.RequestId != call.RequestId || reply.Nonce != call.Nonce
            || reply.Payload.Length > call.MaximumReplyBytes
            || reply.EndpointDiscovery is not { TransportReady: true } discovery
            || discovery.VoterId != owner.VoterIds[RemoteResourceLifetimeReadProtocol.FirstVoter]
            || discovery.Incarnation != owner.Incarnation || discovery.ClusterId != options.ClusterId
            || discovery.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || discovery.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || discovery.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal
            || !PhysicalGroupAddress.CanonicalAddress(discovery.SiloAddress, membership)
            || !ValidResult(reply))
        {
            throw RemoteResourceLifetimeReadValidation.Invalid();
        }
    }

    private static bool ValidResult(RemoteResourceLifetimeReadReplyV2 reply)
        => reply.Error is { } error
            ? reply.Payload.IsEmpty && Enum.IsDefined(error) && reply.SafeDetail is { } detail
                && detail == McpReplyProtocol.SafeDetail(error, detail)
            : !reply.Payload.IsEmpty && reply.SafeDetail is null;
}
