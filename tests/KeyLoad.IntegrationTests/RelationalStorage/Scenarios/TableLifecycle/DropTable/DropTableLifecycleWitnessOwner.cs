using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.IO;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleWitnessOwner
{
    internal static DropLifecycleRf3ReceiptProof? Read(string root, string node, string voter,
        PhysicalShardRecord owner, DropTableLifecycleTable table, Guid parentId,
        ZoneTreeConservationBudget work, CancellationToken token)
    {
        var failures = new List<Exception>();
        FileStream? ownerLock = null;
        ZoneTreeStore? canonical = null;
        ZoneTreeStore? replica = null;
        DropLifecycleRf3ReceiptProof? result = null;
        ServerFailureObserver.Observe(() =>
        {
            work.Check();
            var path = NativeBlobWholePaths.RequireNode(root, node);
            var options = IntegrationExecutionOptions.StorageExecution();
            ownerLock = OfflineRegularFile.Open(Path.Combine(path, PartitionStoreProtocol.OwnershipFile),
                FileAccess.ReadWrite, FileShare.None, options.Value.StreamBufferBytes);
            canonical = DropTableLifecycleWitnessStores.Open(NativeBlobWholePaths.Canonical(path), owner);
            replica = DropTableLifecycleWitnessStores.Open(NativeBlobWholePaths.Replica(path), owner);
            DropTableLifecycleWitnessStores.Require(canonical, replica, owner, table);
            result = DropTableLifecycleWitnessHistory.Read(canonical, replica,
                NativeBlobWholePaths.Replica(path), owner, voter,
                table, parentId, work, token);
        }, failures);
        if (replica is { } journal)
        {
            ServerFailureObserver.Observe(journal.Dispose, failures);
        }
        if (canonical is { } database)
        {
            ServerFailureObserver.Observe(database.Dispose, failures);
        }
        if (ownerLock is { } physicalLock)
        {
            ServerFailureObserver.Observe(physicalLock.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }
}
