namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal sealed class AnalyticsNativeMapColdTests
{
    [Test]
    public async Task JsonAnalyticsAppendNativeRowsAndFiveAggregatesRetainOriginalReceiptAcrossTwoColdReopens()
        => await AnalyticsNativeMapColdFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
