namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal sealed class AnalyticsBoundsTests
{
    [Test]
    public Task GroupOverflowRejectsTheWholeQueryThenCompleteBoundedQueryIsHealthy()
        => AnalyticsBoundsFlow.GroupsAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    public Task FiniteInputSumOverflowRefusesAllRowsThenCountOnlyQueryIsHealthy()
        => AnalyticsBoundsFlow.SumAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    public Task CanonicalEmptyWhitespaceControlAndUnicodeDimensionsRemainDistinctAfterRebuildUpdateAndDelete()
        => AnalyticsBoundsDimensionsFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    public Task DimensionEncodingRetainsFreshFieldUseAndRawReadIndependence()
        => AnalyticsBoundsPrivacyFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
