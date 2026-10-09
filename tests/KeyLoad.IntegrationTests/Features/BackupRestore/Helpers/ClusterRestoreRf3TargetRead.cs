using System.Collections.Immutable;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Actual joined native cold authority, independently expected complete restored metadata.</summary>
internal static class ClusterRestoreRf3TargetRead
{
    private const int FirstNode = 0;
    private const int GroupMembers = 3;

    internal static async Task<ImmutableArray<ClusterRestoreRf3OperatorNode>> RequireAsync(ClusterRestoreRf3Fixture target,
        ImmutableArray<ClusterBackupOwnerReceipt> originals, bool dispatchPaused)
    {
        await target.StopAsync().ConfigureAwait(false);
        var failures = new List<Exception>();
        var nodes = ImmutableArray.CreateBuilder<ClusterRestoreRf3OperatorNode>();
        for (var index = FirstNode; index < ClusterRestoreRf3Protocol.Nodes.Length; index++)
        {
            var mapping = target.Mappings[index / GroupMembers];
            var original = originals.Single(receipt => receipt.Cut.Owner.PhysicalShardId == mapping.Source.PhysicalShardId);
            ZoneTreeStore? store = null;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                store = new(new(Path.Combine(target.DataRoot, ClusterRestoreRf3Protocol.Nodes[index],
                    ClusterRestoreRf3Protocol.DatabaseDirectory)), IntegrationExecutionOptions.StorageExecution(),
                    IntegrationExecutionOptions.PointCacheExecution());
                nodes.Add(await ClusterRestoreRf3TargetAuthority.RequireAsync(store, target.Mappings,
                    originals, original, mapping, nodes, index, dispatchPaused, target.OperationId, target.ExpectedSignerFingerprint).ConfigureAwait(false));
            }, failures).ConfigureAwait(false);
            if (store is { } owned)
            { ServerFailureObserver.Observe(owned.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return nodes.ToImmutable();
    }
}
