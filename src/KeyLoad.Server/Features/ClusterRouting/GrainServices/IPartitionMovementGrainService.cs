using Orleans.CodeGeneration;
using Orleans.Services;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Native directed access to the silo-local partition movement receiver.</summary>
[Alias(PartitionMovementGrainServiceProtocol.ServiceAlias), Version(PartitionMovementGrainServiceProtocol.InterfaceVersion)]
internal interface IPartitionMovementGrainService : IGrainService
{
    /// <summary>Dispatches one bounded authenticated movement operation to this silo.</summary>
    [Alias(PartitionMovementGrainServiceProtocol.ExchangeAlias)]
    Task<PartitionMovementGrainServiceReply> ExchangeAsync(PartitionMovementGrainServiceRequest request,
        CancellationToken cancellationToken);

    /// <summary>Joins one authenticated original physical-owner probe independently of movement readiness.</summary>
    [Alias(PartitionMovementGrainServiceProtocol.ProbeOwnerAlias)]
    Task<PartitionMovementGrainServiceReply> ProbeOwnerAsync(PartitionMovementGrainServiceRequest request,
        CancellationToken cancellationToken);

    /// <summary>Returns this actual silo generation bound to a fresh discovery nonce.</summary>
    [Alias(PartitionMovementGrainServiceProtocol.DiscoverAlias)]
    Task<PartitionMovementGrainServiceDiscovery> DiscoverAsync(Guid nonce, CancellationToken cancellationToken);
}
