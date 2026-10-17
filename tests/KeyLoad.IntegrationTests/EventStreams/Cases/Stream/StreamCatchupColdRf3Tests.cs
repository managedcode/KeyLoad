using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class StreamCatchupColdRf3Tests(ClusterFixture fixture)
{
    [Test]
    public Task OriginalEmptyTailAppendRaceRetainsEveryEventAndReceiptAcrossColdPublicContinuation()
        => StreamCatchupColdRf3Flow.RunAsync(fixture);
}
