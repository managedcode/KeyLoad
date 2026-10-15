using System.Net;
using KeyLoad.Replication;
using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeReopen
{
    internal static async Task<ControlledPartitionMovementNativeHosts> OpenAsync(
        ControlledPartitionMovementNativeHostInput input, string originalRoot, int firstRetainedNode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = ControlledPartitionMovementNativeHostSettings.Restore(input, originalRoot);
        for (var index = firstRetainedNode; index < ControlledPartitionMovementNativeHostEndpoints.OwnerCount; index++)
        {
            var root = settings.DirectoryPath(index);
            if (!Directory.Exists(Path.Combine(root, PartitionStoreProtocol.CanonicalDirectory))
                || !Directory.Exists(Path.Combine(root, ReplicaProtocol.ReplicaDirectory)))
            {
                throw ControlledPartitionMovementNativeHostInputValidation.Invalid();
            }
        }
        var owner = new ControlledPartitionMovementNativeHosts(settings,
            new(new IPEndPoint(IPAddress.Loopback, input.PrimarySiloPort)));
        var retainedRoot = ControlledPartitionMovementNativeFailureRoot.Capture(settings);
        try
        {
            await owner.CreateNodesAsync(cancellationToken).ConfigureAwait(false);
            await ControlledPartitionMovementNativeHosts.JoinOriginalAsync(Task.WhenAll(owner.Nodes.Select(
                node => node.StartAsync(cancellationToken)))).ConfigureAwait(false);
            return owner;
        }
        catch (Exception primary)
        {
            owner.RetainCreationFailure(primary);
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(() => retainedRoot.Attach(primary), failures);
            await ServerFailureObserver.ObserveAsync(() => owner.CloseRetainingRootAsync().AsTask(), failures)
                .ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }
}
