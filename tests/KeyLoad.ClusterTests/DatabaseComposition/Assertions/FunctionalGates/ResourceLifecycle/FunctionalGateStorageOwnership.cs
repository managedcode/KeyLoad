using KeyLoad.ClusterTests.StorageRecovery;

namespace KeyLoad.ClusterTests.DatabaseComposition;

/// <summary>Checks release of the original node-local storage locks without changing their owned roots.</summary>
internal static class FunctionalGateStorageOwnership
{
    internal static void RequireReleased(FunctionalGateFixture fixture)
    {
        foreach (var node in fixture.NodeNames)
        {
            StorageOwnershipFiles.AssertExclusive(Path.Combine(fixture.Root, node, "node.owner.lock"));
            StorageOwnershipFiles.AssertExclusive(Path.Combine(fixture.Root, node, "database", "owner.lock"));
        }
    }
}
