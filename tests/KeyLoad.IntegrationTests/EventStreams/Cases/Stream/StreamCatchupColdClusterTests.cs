using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class StreamCatchupColdClusterTests(ClusterFixture fixture)
{
    [Test]
    public Task OriginalEmptyTailAppendRaceRetainsEveryEventAndReceiptAcrossColdPublicContinuation()
        => StreamCatchupColdClusterFlow.RunAsync(fixture);
}
