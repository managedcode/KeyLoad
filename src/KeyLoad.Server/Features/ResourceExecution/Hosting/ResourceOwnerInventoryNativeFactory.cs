using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceOwnerInventoryNativeFactory
{
    internal static async Task<ResourceOwnerInventoryNativeRuntime> CreateAsync(OrleansNode node,
        PartitionHost partition, ServerRuntimeOptions options, ReplicaSiloDiscoveryState local, TimeProvider clock)
    {
        ResourceOwnerInventoryNativeClient? client = null;
        ResourceOwnerInventoryNativeRuntime? result = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            var movement = node.BorrowPartitionMovementTransport();
            var authority = new ResourceOwnerInventoryNativeAuthority(node, partition, options.Node, movement, local);
            var trust = new ResourceOwnerInventoryNativeTrust(partition, options.Node, options.Core.DatabaseLimits);
            var authentication = new PhysicalOwnerStartupRequests(node, options.Node, options.McpExecution);
            var reads = new ResourceOwnerInventorySignedReads(node, partition, authentication, clock);
            client = new(partition, movement, authority, trust, reads, options.Core.DatabaseLimits,
                options.GrainRouting, options.Node, local, clock);
            var receiver = new ResourceOwnerInventoryReceiver(authority, trust, reads, options.Core.DatabaseLimits,
                options.GrainRouting, clock);
            result = new(options.GrainRouting, receiver, client, clock);
        }, failures);
        if (result is null && client is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => client.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryNativeProtocol.Unavailable);
    }
}
