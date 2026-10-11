using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
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
        var configured = options.MembershipAuthority;
        if (destination.Owner is null || destination.Endpoints.IsDefaultOrEmpty
            || destination.Owner.PhysicalShardId != configured.TrustedGroupPhysicalShardId
            || destination.Owner.Incarnation != configured.TrustedGroupIncarnation
            || !destination.Owner.VoterIds.SequenceEqual(configured.TrustedGroupVoterIds, StringComparer.Ordinal)
            || !destination.Endpoints.SequenceEqual(configured.TrustedGroupVoterIds.Select(
                static voter => new Uri(voter, UriKind.Absolute).AbsoluteUri), StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable); }
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
            || !ReplicaMembershipAuthorityValidation.CanonicalAddress(discovery.SiloAddress, membership))
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
        var address = SiloAddress.FromParsableString(discovery.SiloAddress);
        if (address.Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
        return address;
    }
}
