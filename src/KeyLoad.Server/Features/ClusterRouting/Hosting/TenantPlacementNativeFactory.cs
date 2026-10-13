using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class TenantPlacementNativeFactory
{
    internal static async Task<TenantPlacementNativeRuntime?> CreateAsync(OrleansNode node,
        PartitionHost partition, ServerRuntimeOptions options, PartitionMovementRuntime? movement,
        TimeProvider clock)
    {
        if (!options.TenantPlacement.Value.Enabled)
        { return null; }
        if (movement is null || !options.PartitionMovement.Value.Enabled
            || !options.Node.Value.PhysicalGroupTrust.RegisterPhysicalOwners)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, TenantPlacementCapacityProtocol.Unavailable); }
        PartitionMovementPeerAdmission? peer = null;
        OwnerCapacityPeerAdmission? admission = null;
        OwnerCapacityResourceSampler? sampler = null;
        OwnerCapacityNativeLifetime? capacity = null;
        PlacementIntentAdmissions? applies = null;
        TenantPlacementNativeRuntime? result = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            peer = new(options.Node, partition.Database, options.ReplicaConfiguration,
                options.GrainRouting, options.Membership, options.PartitionMovement, clock);
            admission = new(peer, options.TenantPlacement);
            sampler = new(partition.DirectoryPath, clock, options.TenantPlacement);
            var reads = new OwnerCapacitySignedReads(node, partition, clock);
            var receiver = new OwnerCapacityReceiver(node, partition, admission, reads,
                sampler, peer, options.TenantPlacement);
            capacity = new(options.GrainRouting, receiver, sampler, admission, clock);
            applies = new(options.TenantPlacement);
            var collector = new PlacementCapacityCollector(node, partition, peer, clock);
            var phases = new PlacementIntentPhaseExecution(node, partition, clock);
            var planner = new TenantPlacementPlanner(partition, movement, collector, clock, options.TenantPlacement);
            var executor = new PlacementIntentExecutor(node, partition, movement, applies,
                collector, phases, clock);
            var persistence = new PlacementIntentPersistenceExecution(partition, movement, collector, clock, options.Core.DatabaseLimits);
            result = new(capacity, applies, peer, movement, planner, executor, persistence,
                options.Node.Value.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Control);
        }, failures);
        if (result is null)
        {
            if (capacity is not null)
            { await ServerFailureObserver.ObserveAsync(() => capacity.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
            else
            {
                if (sampler is not null)
                { await ServerFailureObserver.ObserveAsync(() => sampler.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
                ServerFailureObserver.Observe(() => admission?.Dispose(), failures);
            }
            if (applies is not null)
            { await ServerFailureObserver.ObserveAsync(() => applies.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
            ServerFailureObserver.Observe(() => peer?.Dispose(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }
}
