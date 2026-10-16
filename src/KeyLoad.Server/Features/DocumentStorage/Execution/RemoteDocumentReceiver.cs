using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.QueryExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentReceiver(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> nodeOptions, IOptions<GrainRoutingOptions> routingOptions,
    IOptions<OrleansMembershipOptions> membershipOptions, TimeProvider clock)
{
    internal void Validate(RemoteDocumentCallV1 call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = node.Discovery?.Read() ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        var settings = nodeOptions.Value.PhysicalGroupTrust;
        var now = clock.GetUtcNow();
        if (!settings.RemoteDocumentReads || !settings.RegisterPhysicalOwners
            || (call.QueryLeaf is not null || call.SearchLeaf is not null) && !settings.RemotePartitionQueries
            || settings.Role != PhysicalGroupTrustRole.Destination
            || call.Version != RemoteDocumentProtocol.Version || call.RequestId == Guid.Empty
            || !RemoteModelNativeIdentity.ValidNonce(call.Nonce)
            || !PhysicalOwnerConfiguredTuples.Control(nodeOptions.Value, partition).Owner.VoterIds.Contains(call.CallerVoter, StringComparer.Ordinal)
            || !PhysicalGroupAddress.CanonicalAddress(call.CallerSiloAddress, membershipOptions)
            || call.ExpiresAt <= now || call.ExpiresAt > now + routingOptions.Value.RequestLifetime
            || call.Source is null || call.Fence is null || !RemotePartitionQueryScope.Valid(call)
            || !PhysicalOwnerEntryValidation.SameOwner(call.Source,
                PhysicalOwnerConfiguredTuples.Control(nodeOptions.Value, partition).Owner)
            || !PhysicalOwnerEntryValidation.Same(call.Fence.Destination,
                PhysicalOwnerConfiguredTuples.Local(nodeOptions.Value, partition))
            || call.Fence.Placement.IsFallback || call.Fence.Placement.Partition != RemotePartitionQueryScope.Partition(call)
            || call.Fence.Placement.PhysicalShardId != nodeOptions.Value.PhysicalShardId
            || call.Fence.Placement.Incarnation != call.Fence.Destination.Owner.Incarnation
            || call.Fence.Placement.PlacementEpoch != call.Fence.Destination.Owner.PlacementEpoch
            || !call.Fence.Placement.VoterIds.SequenceEqual(call.Fence.Destination.Owner.VoterIds, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
    }

}
