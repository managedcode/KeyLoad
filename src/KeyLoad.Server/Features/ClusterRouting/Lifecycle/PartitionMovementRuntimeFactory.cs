using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Constructs one native runtime and joins its original cleanup if initialization fails.</summary>
internal static class PartitionMovementRuntimeFactory
{
    internal static async Task<PartitionMovementRuntime?> CreateAsync(OrleansNode node, PartitionHost partition,
        ServerRuntimeOptions options, NativeRequestWorkOwner work, TimeProvider clock)
    {
        if (!options.PartitionMovement.Value.Enabled)
        { return null; }
        if (!options.Node.Value.MembershipAuthority.RegisterPhysicalOwners
            || !options.Node.Value.MembershipAuthority.RemoteDocumentReads)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMovementProtocol.Unavailable); }
        var runtime = new PartitionMovementRuntime(work);
        try
        {
            runtime.Initialize(node, partition, options, work, clock);
            return runtime;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(() => runtime.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

}
