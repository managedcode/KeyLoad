using KeyLoad.Replication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochRf3ReceiptReader
{
    internal static ServerNodeUpgradeReceipt Read(string directory)
        => ServerNodeUpgradeStage.ReadPrepared(directory);

    internal static ReplicaSnapshot VerifyCurrentSnapshot(string source, string destination,
        NodeEpochRf3Profile profile, string nodeName)
    {
        var options = NodeEpochRf3OfflineOptions.Create(destination, profile, nodeName);
        var receipt = ServerNodeFormatUpgrade.VerifyPrepared(source, options);
        ServerNodeUpgradeLocks? sourceLocks = null;
        ServerNodeUpgradeLocks? targetLocks = null;
        Exception? primary = null;
        try
        {
            sourceLocks = ServerNodeUpgradeLocks.Acquire(source);
            targetLocks = ServerNodeUpgradeLocks.Acquire(destination);
            var originalSource = ServerNodeUpgradeInventory.Capture(source, sourceLocks.Held);
            var originalTarget = ServerNodeUpgradeInventory.Capture(destination, targetLocks.Held);
            var current = ServerNodeUpgradeVerifier.Verify(destination, receipt, options, published: true);
            originalSource.RequireSame(ServerNodeUpgradeInventory.Capture(source, sourceLocks.Held));
            originalTarget.RequireSame(ServerNodeUpgradeInventory.Capture(destination, targetLocks.Held));
            return current.Snapshot
                ?? throw new InvalidDataException("The stopped current RF3 node has no verified published snapshot.");
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        { DisposeLocks(targetLocks, sourceLocks, primary); }
    }

    private static void DisposeLocks(ServerNodeUpgradeLocks? target, ServerNodeUpgradeLocks? source, Exception? primary)
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => target?.Dispose(), failures);
        ServerFailureObserver.Observe(() => source?.Dispose(), failures);
        if (failures.Count > 0)
        {
            if (primary is not null)
            { failures.Insert(0, primary); }
            throw new AggregateException("Stopped-node observation and lock cleanup failed.", failures);
        }
    }
}
