using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Dispatches privately signed lifetime-bound document and query reads to their owning capability.</summary>
internal static class GrainLifetimeReadCapabilities
{
    internal static bool Handles(GrainReadKind kind)
        => kind is GrainReadKind.OwnedDocumentV2 or GrainReadKind.PartitionQueryLeafV2 or GrainReadKind.DistributedSearchLeafV2;

    internal static object Execute(DatabaseEngine database, IServiceProvider services,
        TimeProvider clock, PrincipalRecord principal, DecodedGrainRequest request,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
        => request.Envelope.ReadKind switch
        {
            GrainReadKind.OwnedDocumentV2 => DocumentLifetimeReadCapabilities.Execute(
                database, services, clock, principal, request, binding, cancellationToken),
            GrainReadKind.PartitionQueryLeafV2 or GrainReadKind.DistributedSearchLeafV2 => QueryLifetimeReadCapability.Execute(
                database, services, clock, principal, request, binding, cancellationToken),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
}
