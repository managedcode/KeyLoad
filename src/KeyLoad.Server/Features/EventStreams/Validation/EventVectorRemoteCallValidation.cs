using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorRemoteCallValidation(PartitionHost partition,
    IOptions<NodeOptions> nodeOptions, IOptions<GrainRoutingOptions> routingOptions,
    IOptions<OrleansMembershipOptions> membershipOptions, TimeProvider clock)
{
    internal RegisteredPhysicalOwnerV1 LocalOwner
        => PhysicalOwnerConfiguredTuples.Local(nodeOptions.Value, partition);

    internal RegisteredPhysicalOwnerV1 ControlOwner
        => PhysicalOwnerConfiguredTuples.Control(nodeOptions.Value, partition);

    internal int GroupOrdinal
    {
        get
        {
            const int ControlGroupOrdinal = 0;
            const int DestinationGroupOrdinal = 1;
            var local = LocalOwner.Owner;
            if (PhysicalOwnerEntryValidation.SameOwner(local, ControlOwner.Owner))
            { return ControlGroupOrdinal; }
            var destination = PhysicalOwnerConfiguredTuples.Destination(nodeOptions.Value, partition);
            if (PhysicalOwnerEntryValidation.SameOwner(local, destination.Owner))
            { return DestinationGroupOrdinal; }
            throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable);
        }
    }

    internal void Require(EventVectorRemoteCall call, CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        originalCancellationToken.ThrowIfCancellationRequested();
        var settings = nodeOptions.Value.PhysicalGroupTrust;
        var control = ControlOwner;
        var now = clock.GetUtcNow();
        if (!settings.RemoteDocumentReads || !settings.RegisterPhysicalOwners
            || settings.Role is not (global::KeyLoad.Server.Features.ClusterRouting.PhysicalGroupTrustRole.Control
                or global::KeyLoad.Server.Features.ClusterRouting.PhysicalGroupTrustRole.Destination)
            || call.Version != EventVectorRemoteProtocol.Version
            || call.Purpose != EventVectorRemoteProtocol.Purpose || !Enum.IsDefined(call.Action)
            || call.RequestId == Guid.Empty || !global::KeyLoad.Server.Features.ClusterRouting.PhysicalOwnerProbeNativeIdentity.ValidNonce(call.Nonce)
            || !control.Owner.VoterIds.Contains(call.CallerVoter, StringComparer.Ordinal)
            || !global::KeyLoad.Server.Features.ClusterRouting.PhysicalGroupAddress.CanonicalAddress(call.CallerSiloAddress, membershipOptions)
            || call.ExpiresAt <= now || call.ExpiresAt > now + routingOptions.Value.RequestLifetime
            || call.Source is null || !PhysicalOwnerEntryValidation.SameOwner(call.Source, control.Owner))
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
    }
}
