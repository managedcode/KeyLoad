using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.TimeSeries;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TimeSeriesAggregateScanBoundClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task AggregateScanExhaustionRefusesWithoutPartialResultWhileInclusiveRangeTruncatesAndHealthyReplaysSurviveTwoColdCycles()
    {
        using var deadline = McpCallerDeadline.Create();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, deadline.Token);
        await TimeSeriesAggregateScanBoundClusterTrial.RunAsync(fixture, linked.Token);
    }
}
