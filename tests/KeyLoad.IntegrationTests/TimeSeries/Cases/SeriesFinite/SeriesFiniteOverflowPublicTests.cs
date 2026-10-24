using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.TimeSeries;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SeriesFiniteOverflowPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task FiniteSamplesKeepExactPublicStateThroughOverflowColdRetentionRepairAndOriginalReceiptReplay()
    {
        using var deadline = McpCallerDeadline.Create();
        await SeriesFiniteOverflowPublicTrial.RunAsync(fixture, deadline.Token);
    }
}
