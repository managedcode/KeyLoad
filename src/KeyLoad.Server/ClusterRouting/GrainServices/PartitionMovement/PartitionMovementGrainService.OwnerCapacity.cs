using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementGrainService
{
    private OwnerCapacityNativeLifetime? capacity;

    public Task<OwnerCapacityNativeReply> ReadCapacityAsync(OwnerCapacityNativeRequest request,
        CancellationToken cancellationToken)
        => capacity is { } active ? active.ReadAsync(request, cancellationToken)
            : Task.FromResult(new OwnerCapacityNativeReply(TenantPlacementPolicyProtocol.Version,
                request?.RequestId ?? Guid.Empty, request?.Nonce ?? Guid.Empty, null,
                ErrorCode.UnsupportedCapability, TenantPlacementCapacityProtocol.Unavailable, string.Empty));
}
