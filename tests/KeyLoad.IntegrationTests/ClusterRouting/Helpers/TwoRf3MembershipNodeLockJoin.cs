using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TwoRf3MembershipNodeLockJoin
{
    internal static void AssertAll(TwoRf3MembershipWave wave)
    {
        var root = wave.dataRoot ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.InvalidRoot);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, node, "node.owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, node, "database", "owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, node, "replica", "owner.lock"));
        }
        wave.nodeLocksReleased = true;
    }
}
