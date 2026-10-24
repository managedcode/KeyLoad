namespace KeyLoad.UnitTests.AnalyticsStore;

internal sealed class AnalyticsMapColdTests
{
    [Test]
    public async Task JsonAnalyticsAppendRowsAndFiveAggregatesRetainOriginalReceiptAcrossTwoColdReopens()
        => await AnalyticsMapColdFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
