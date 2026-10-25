using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.TimeSeries;

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
