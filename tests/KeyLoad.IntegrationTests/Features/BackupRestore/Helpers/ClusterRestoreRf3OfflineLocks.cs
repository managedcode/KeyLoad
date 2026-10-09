using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Checks original existing offline operator owners after AppHost/process/readers have joined.</summary>
internal static class ClusterRestoreRf3OfflineLocks
{
    internal static void Require(ClusterRestoreRf3Fixture target, List<Exception> failures)
    {
        var owner = target.DataRoot + ClusterRestoreRf3ResumeProtocol.OperationOwnerSuffix;
        if (File.Exists(owner))
        { ServerFailureObserver.Observe(() => NodeEpochRf3OfflineFiles.AssertExclusive(owner), failures); }
        foreach (var root in new[] { target.DataRoot, Path.Combine(ClusterRestoreRf3RetainedCut.OperationRoot(target),
            ClusterRestoreRf3ResumeProtocol.NodesDirectory) })
        {
            foreach (var node in ClusterRestoreRf3Protocol.Nodes)
            {
                var database = Path.Combine(root, node, ClusterRestoreRf3Protocol.DatabaseDirectory);
                RequireExisting(database, failures);
                RequireExisting(database + ClusterRestoreRf3ResumeProtocol.SlotSuffix, failures);
            }
        }
    }

    private static void RequireExisting(string database, List<Exception> failures)
    {
        foreach (var name in new[] { ClusterRestoreRf3ResumeProtocol.DatabaseOwner, ClusterRestoreRf3ResumeProtocol.SlotOwner })
        {
            var path = Path.Combine(database, name);
            if (File.Exists(path))
            { ServerFailureObserver.Observe(() => NodeEpochRf3OfflineFiles.AssertExclusive(path), failures); }
        }
    }
}
