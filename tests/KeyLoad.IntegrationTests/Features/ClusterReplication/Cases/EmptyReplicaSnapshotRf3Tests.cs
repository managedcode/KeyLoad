using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>AC-REP-004: genuine erased follower installs a snapshot and ordered tail with SDK/official MCP parity.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class EmptyReplicaSnapshotRf3Tests(ClusterFixture fixture)
{
    [Test]
    public Task ErasedFollowerInstallsNativeSnapshotAndOrderedTailBeforeCompletePublicReplayAndHealthyContinuation()
        => EmptyReplicaSnapshotScenario.RunAsync(fixture);
}
