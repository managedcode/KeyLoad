using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Dispatches lifetime-bound query phases to their owning capability.</summary>
internal static class GrainLifetimeReadCapabilities
{
    internal static bool Handles(GrainReadKind kind)
        => kind is GrainReadKind.PartitionQueryLeaf or GrainReadKind.DistributedSearchLeaf;

    internal static object Execute(DatabaseEngine database, IServiceProvider services,
        TimeProvider clock, PrincipalRecord principal, DecodedGrainRequest request,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
        => request.Envelope.ReadKind switch
        {
            GrainReadKind.PartitionQueryLeaf or GrainReadKind.DistributedSearchLeaf => QueryLifetimeReadCapability.Execute(
                database, services, clock, principal, request, binding, cancellationToken),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
}
