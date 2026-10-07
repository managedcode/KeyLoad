using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ClusterFixtureLocalImageLifecycle
{
    private const string ShutdownFailure = "Owned local RF3 image cleanup requires joined node shutdown.";
    private const string NodeOwnerLock = "node.owner.lock";
    private const string DatabaseDirectory = "database";
    private const string ReplicaDirectory = "replica";
    private const string StoreOwnerLock = "owner.lock";
    internal static readonly string[] ExpectedNames = Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber,
        ClusterFixtureProtocol.NodeCount).Select(ClusterFixtureProtocol.NodeName).ToArray();

    internal static async Task JoinAsync(LocalRf3ImageTestSession session, string root, bool stopped,
        bool neverBuilt, List<Exception> failures)
    {
        if (!neverBuilt && (!stopped || failures.Count > 0))
        {
            failures.Add(new InvalidOperationException(ShutdownFailure));
            await ServerFailureObserver.ObserveAsync(() => session.DisposeAsync(removeImage: false).AsTask(), failures)
                .ConfigureAwait(false);
            return;
        }
        if (!neverBuilt)
        {
            ServerFailureObserver.Observe(() => AssertLocksReleased(root), failures);
        }
        await ServerFailureObserver.ObserveAsync(
            () => session.DisposeAsync(removeImage: failures.Count == 0).AsTask(), failures).ConfigureAwait(false);
    }

    private static void AssertLocksReleased(string root)
    {
        foreach (var node in ExpectedNames)
        {
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, node, NodeOwnerLock));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, node, DatabaseDirectory, StoreOwnerLock));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, node, ReplicaDirectory, StoreOwnerLock));
        }
    }
}
