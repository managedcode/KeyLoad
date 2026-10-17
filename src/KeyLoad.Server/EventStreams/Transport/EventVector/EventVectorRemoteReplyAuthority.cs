using KeyLoad.Clustering;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorRemoteReplyAuthority
{
    private const int SelectedVoter = 0;

    internal static void RequireDestination(RegisteredPhysicalOwnerV1 destination, NodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);
        var configured = options.PhysicalGroupTrust;
        if (configured.Role != global::KeyLoad.Server.Features.ClusterRouting.PhysicalGroupTrustRole.Control || !configured.RemoteDocumentReads
            || !configured.RegisterPhysicalOwners || destination.Owner is null || destination.Endpoints.IsDefaultOrEmpty
            || destination.Owner.PhysicalShardId != configured.ForeignPhysicalShardId
            || destination.Owner.Incarnation != configured.ForeignIncarnation
            || !destination.Owner.VoterIds.SequenceEqual(configured.ForeignVoterIds, StringComparer.Ordinal)
            || !destination.Endpoints.SequenceEqual(configured.ForeignVoterIds.Select(
                static voter => new Uri(voter, UriKind.Absolute).AbsoluteUri), StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable);
        }
    }

    internal static SiloAddress Require(EventVectorRemoteReply reply, EventVectorRemoteCall originalCall,
        RegisteredPhysicalOwnerV1 destination, NodeOptions options,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        CancellationToken originalCancellationToken)
    {
        originalCancellationToken.ThrowIfCancellationRequested();
        RequireDestination(destination, options);
        var owner = destination.Owner;
        if (originalCall.ExpiresAt <= clock.GetUtcNow()
            || reply.ReceiverDiscovery is not { TransportReady: true } discovery
            || discovery.VoterId != owner.VoterIds[SelectedVoter] || discovery.Incarnation != owner.Incarnation
            || discovery.ClusterId != options.ClusterId
            || discovery.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || discovery.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || discovery.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal
            || !global::KeyLoad.Server.Features.ClusterRouting.PhysicalGroupAddress.CanonicalAddress(discovery.SiloAddress, membership))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid);
        }
        return SiloAddress.FromParsableString(discovery.SiloAddress);
    }
}
