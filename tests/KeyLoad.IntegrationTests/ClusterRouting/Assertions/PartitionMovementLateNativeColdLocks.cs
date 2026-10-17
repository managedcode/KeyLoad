using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementLateNativeColdLocks
{
    internal static void RequireReleased(IReadOnlyList<PartitionMovementLateNativeNode> nodes)
    {
        foreach (var node in nodes.Where(node => node.HasPublishedStorage))
        {
            var directory = node.Partition.DirectoryPath;
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, PartitionStoreProtocol.OwnershipFile));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, PartitionStoreProtocol.CanonicalDirectory,
                ZoneTreePersistenceFormat.OwnerLockFileName));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, ReplicaProtocol.ReplicaDirectory,
                ZoneTreePersistenceFormat.OwnerLockFileName));
        }
    }
}
