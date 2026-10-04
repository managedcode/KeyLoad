using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class NodeEpochFileReadiness
{
    private const string DatabaseDirectory = "database";
    private const string ReplicaDirectory = "replica";

    internal static async Task WaitAsync(string nodeRoot, CancellationToken cancellationToken)
    {
        await KilledProcessFileReadiness.WaitAsync(Path.Combine(nodeRoot, DatabaseDirectory), cancellationToken);
        await KilledProcessFileReadiness.WaitAsync(Path.Combine(nodeRoot, ReplicaDirectory), cancellationToken);
        AssertNativeHandlesReleased(nodeRoot);
    }

    internal static void AssertNativeHandlesReleased(string nodeRoot)
    {
        AssertStoreHandlesReleased(nodeRoot, DatabaseDirectory);
        AssertStoreHandlesReleased(nodeRoot, ReplicaDirectory);
        AssertNodeOwnerReleased(nodeRoot);
    }

    private static void AssertStoreHandlesReleased(string nodeRoot, string storeDirectory)
        => EpochUpgradeFileInventory.AssertNativeHandlesReleased(Path.Combine(nodeRoot, storeDirectory));

    private static void AssertNodeOwnerReleased(string nodeRoot)
    {
        var ownerPath = Path.Combine(nodeRoot, ServerNodeUpgradeProtocol.NodeOwner);
        using var owner = new FileStream(ownerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
}
