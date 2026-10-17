using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static partial class PartitionMovementPublicParentRf3Cut
{
    internal static Task<PartitionMovementPublicParentRf3NativeCut[]> StopAndReadAsync(
        TwoRf3MembershipWave wave, PartitionMoveRequest request, CancellationToken cancellationToken)
        => StopAndReadAsync(wave, request, Guid.Empty, cancellationToken);

    internal static async Task<PartitionMovementPublicParentRf3NativeCut[]> StopAndReadAsync(
        TwoRf3MembershipWave wave, PartitionMoveRequest request, Guid originalPhaseId,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => wave.RemoteRuntime.KillAsync(node,
                "public-parent-joined-cold-original-cut", cancellationToken), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var root = Path.Combine(wave.OwnedDataRoot, node);
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "node.owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "database", "owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "replica", "owner.lock"));
        }
        return TwoRf3MembershipProtocol.Nodes.Select(node => ReadStopped(wave, node, request, originalPhaseId)).ToArray();
    }

    internal static async Task RestartAsync(TwoRf3MembershipWave wave, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await wave.RemoteRuntime.BeginRestartAsync(node,
                cancellationToken), failures).ConfigureAwait(false);
        }
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => wave.RemoteRuntime.RestartAsync(node,
                cancellationToken), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static Task RequireCompactedAsync(PartitionMovementPublicParentRf3NativeCut[] cuts)
        => PartitionMovementPublicParentRf3CompactionAssertions.RequireAsync(cuts);

    internal static PartitionMovementPublicParentRf3NativeCut ReadStopped(TwoRf3MembershipWave wave,
        string node, PartitionMoveRequest request, Guid originalPhaseId)
    {
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        PartitionMovementPublicParentRf3NativeCut? result = null;
        ServerFailureObserver.Observe(() =>
        {
            store = new(new(Path.Combine(wave.OwnedDataRoot, node, "database")),
                IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution());
            result = PartitionMovementPublicParentRf3NativeReader.ReadView(node, request, originalPhaseId, store);
        }, failures);
        if (store is { } owned)
        {
            ServerFailureObserver.Observe(owned.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException("The actual stopped parent cut is absent.");
    }

}
