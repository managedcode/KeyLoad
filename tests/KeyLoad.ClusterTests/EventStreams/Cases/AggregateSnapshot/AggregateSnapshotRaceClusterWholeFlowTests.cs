using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class AggregateSnapshotRaceClusterWholeFlowTests(ClusterFixture fixture)
{
    [Test]
    public async Task ConcurrentSdkMcpSnapshotCasPreservesWinnerLoserAndReferenceAfterFreshGrantRepairAndTwoColdWaves()
    {
        using var deadline = McpCallerDeadline.Create();
        await AggregateSnapshotRaceClusterFlow.RunAsync(fixture, deadline.Token);
    }
}
