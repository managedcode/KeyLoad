using Orleans.CodeGeneration;
using Orleans.Services;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Native directed access to one silo's authenticated owner probe, inventory, retirement and observation endpoints.</summary>
[Alias(PartitionMovementGrainServiceProtocol.ServiceAlias), Version(PartitionMovementGrainServiceProtocol.InterfaceVersion)]
internal interface IPartitionMovementGrainService : IGrainService
{
    /// <summary>Joins one authenticated original physical-owner probe independently of movement readiness.</summary>
    [Alias(PartitionMovementGrainServiceProtocol.ProbeOwnerAlias)]
    Task<PartitionMovementGrainServiceReply> ProbeOwnerAsync(PartitionMovementGrainServiceRequest request,
        CancellationToken cancellationToken);

    /// <summary>Reads bounded authoritative inventory through the original authenticated connection path.</summary>
    [Alias(global::KeyLoad.Server.Features.ResourceExecution.ResourceOwnerInventoryNativeProtocol.ServiceMethodAlias)]
    Task<global::KeyLoad.Server.Features.ResourceExecution.ResourceOwnerInventoryNativeReply> ReadResourceInventoryAsync(
        global::KeyLoad.Server.Features.ResourceExecution.ResourceOwnerInventoryNativeRequest request,
        CancellationToken cancellationToken);

    /// <summary>Executes one authenticated durable retirement phase or bounded recovery query.</summary>
    [Alias(global::KeyLoad.Server.Features.ResourceExecution.ResourceRetirementNativeProtocol.ServiceMethodAlias)]
    Task<global::KeyLoad.Server.Features.ResourceExecution.ResourceRetirementNativeReply> ExecuteResourceRetirementAsync(
        global::KeyLoad.Server.Features.ResourceExecution.ResourceRetirementNativeRequest request,
        CancellationToken cancellationToken);

    /// <summary>Returns this actual silo generation bound to a fresh discovery nonce.</summary>
    [Alias(PartitionMovementGrainServiceProtocol.DiscoverAlias)]
    Task<PartitionMovementGrainServiceDiscovery> DiscoverAsync(Guid nonce, CancellationToken cancellationToken);

    /// <summary>Executes, reads or joins only one pre-effect original command observation at this exact silo.</summary>
    [Alias(CommandObservationNativeProtocol.MethodAlias)]
    Task<CommandObservationNativeReply> ExecuteOriginalCommandObservationAsync(
        CommandObservationNativeRequest request, CancellationToken token);

}
