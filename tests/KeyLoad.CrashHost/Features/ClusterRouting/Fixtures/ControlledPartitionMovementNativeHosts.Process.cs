using System.Net;
using System.Collections.Immutable;
using KeyLoad.Server;
using KeyLoad.Replication;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed partial class ControlledPartitionMovementNativeHosts
{
    private const int FirstRetainedNode = 0;

    internal async Task<ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot>> CaptureAsync(
        CancellationToken cancellationToken)
    {
        var snapshots = ImmutableArray.CreateBuilder<ControlledPartitionMovementNativeProcessSnapshot>();
        foreach (var node in nodes)
        { snapshots.Add(await ControlledPartitionMovementNativeProcessCapture.ReadAsync(node, cancellationToken).ConfigureAwait(false)); }
        return snapshots.ToImmutable();
    }

    internal ControlledPartitionMovementNativeHostInput CaptureInput() => settings.CaptureInput();

    internal static async Task<ControlledPartitionMovementNativeHosts> OpenAsync(
        ControlledPartitionMovementNativeHostInput input, string originalRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = ControlledPartitionMovementNativeHostSettings.Restore(input, originalRoot);
        for (var index = FirstRetainedNode; index < ControlledPartitionMovementNativeHostEndpoints.OwnerCount; index++)
        {
            var root = settings.DirectoryPath(index);
            if (!Directory.Exists(Path.Combine(root, PartitionStoreProtocol.CanonicalDirectory))
                || !Directory.Exists(Path.Combine(root, ReplicaProtocol.ReplicaDirectory)))
            { throw ControlledPartitionMovementNativeHostInputValidation.Invalid(); }
        }
        var owner = new ControlledPartitionMovementNativeHosts(settings,
            new(new IPEndPoint(IPAddress.Loopback, input.PrimarySiloPort)));
        var retainedRoot = ControlledPartitionMovementNativeFailureRoot.Capture(settings);
        try
        {
            await owner.CreateNodesAsync().ConfigureAwait(false);
            await JoinOriginalAsync(Task.WhenAll(owner.nodes.Select(
                node => node.StartAsync(cancellationToken)))).ConfigureAwait(false);
            return owner;
        }
        catch (Exception primary)
        {
            owner.creationFailure = primary;
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(() => retainedRoot.Attach(primary), failures);
            await ServerFailureObserver.ObserveAsync(() => owner.CloseRetainingRootAsync().AsTask(), failures)
                .ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }
}
