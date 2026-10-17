using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class AggregateSnapshotRaceRf3WholeFlowTests(ClusterFixture fixture)
{
    [Test]
    public async Task ConcurrentSdkMcpSnapshotCasPreservesWinnerLoserAndReferenceAfterFreshGrantRepairAndTwoColdWaves()
    {
        using var deadline = McpCallerDeadline.Create();
        await AggregateSnapshotRaceRf3Flow.RunAsync(fixture, deadline.Token);
    }
}
