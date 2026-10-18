using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Checks release of the original node-local storage locks without changing their owned roots.</summary>
internal static class FunctionalGateStorageOwnership
{
    internal static void RequireReleased(FunctionalGateFixture fixture)
    {
        foreach (var node in fixture.NodeNames)
        {
            NodeEpochClusterOfflineFiles.AssertExclusive(Path.Combine(fixture.Root, node, "node.owner.lock"));
            NodeEpochClusterOfflineFiles.AssertExclusive(Path.Combine(fixture.Root, node, "database", "owner.lock"));
            NodeEpochClusterOfflineFiles.AssertExclusive(Path.Combine(fixture.Root, node, "replica", "owner.lock"));
        }
    }
}
