using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class StreamCatchupColdClusterTests(ClusterFixture fixture)
{
    [Test]
    public Task OriginalEmptyTailAppendRaceRetainsEveryEventAndReceiptAcrossColdPublicContinuation()
        => StreamCatchupColdClusterFlow.RunAsync(fixture);
}
