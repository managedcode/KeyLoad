using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.GraphTraversal;

internal sealed class RemoteGraphNativeAdmission(PartitionHost partition, IOptions<NodeOptions> options,
    IOptions<OrleansMembershipOptions> membershipOptions, ISiloStatusOracle oracle,
    ReplicaSiloDiscoveryState local, TimeProvider clock)
{
    private readonly ReplicaNativeDiscoveryMembership membership = new(oracle, membershipOptions);

    internal void RequireCurrent(RemoteGraphNativeRequestV1 request)
    {
        RemoteGraphNativeIdentity.RequireOwner(options.Value, partition, request.SourceOwner);
        RemoteGraphNativeIdentity.RequireOwner(options.Value, partition, request.DestinationOwner);
        var expected = PhysicalOwnerConfiguredTuples.Local(options.Value, partition);
        var actual = local.Read();
        if (!PhysicalOwnerEntryValidation.Same(expected, request.DestinationOwner)
            || request.DestinationVoter != partition.Configuration.LocalId || !actual.TransportReady
            || actual.VoterId != request.DestinationVoter || actual.SiloAddress != request.DestinationSiloAddress
            || !membership.IsCurrent(request.CallerVoter, SiloAddress.FromParsableString(request.CallerSiloAddress))
            || !membership.IsCurrent(request.DestinationVoter, SiloAddress.FromParsableString(request.DestinationSiloAddress)))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof);
        }
        if (request.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteGraphNativeProtocol.Unavailable);
        }
    }
}
