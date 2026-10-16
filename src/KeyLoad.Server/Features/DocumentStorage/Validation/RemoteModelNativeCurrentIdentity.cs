using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteModelNativeCurrentIdentity
{
    internal static void Require(NodeOptions options, PartitionHost partition, ReplicaSiloDiscoveryState local,
        ReplicaNativeDiscoveryMembership membership, RemoteModelNativeRequest request)
    {
        var expected = PhysicalOwnerConfiguredTuples.Local(options, partition);
        RemoteModelNativeIdentity.RequireOwner(options, partition, request.SourceOwner);
        if (!PhysicalOwnerEntryValidation.SameOwner(request.DestinationOwner, expected.Owner)
            || request.DestinationVoter != partition.Configuration.LocalId
            || !request.SourceOwner.VoterIds.Contains(request.CallerVoter, StringComparer.Ordinal)
            || !local.Read().TransportReady || request.DestinationSiloAddress != local.RuntimeAddress
            || !membership.IsCurrent(request.CallerVoter, SiloAddress.FromParsableString(request.CallerSiloAddress))
            || !membership.IsCurrent(request.DestinationVoter, SiloAddress.FromParsableString(request.DestinationSiloAddress)))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof);
        }
    }

}
