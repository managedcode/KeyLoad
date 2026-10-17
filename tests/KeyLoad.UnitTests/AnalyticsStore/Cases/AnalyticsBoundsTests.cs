namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal sealed class AnalyticsBoundsTests
{
    [Test]
    public Task NativeGroupOverflowRejectsTheWholeQueryThenCompleteBoundedQueryIsHealthy()
        => AnalyticsBoundsNativeFlow.GroupsAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    public Task CanonicalScanAndReadBytesRejectWholeQueryWithoutChangingAnyCanonicalCut()
        => AnalyticsBoundsSourceFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    public Task FiniteInputSumOverflowRefusesAllRowsThenCountOnlyQueryIsHealthy()
        => AnalyticsBoundsNativeFlow.SumAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    public Task ExactOutputByteLimitRejectsWholeResultThenBoundedNativeQueryIsHealthy()
        => AnalyticsBoundsNativeFlow.ResultAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    public Task CanonicalEmptyWhitespaceControlAndUnicodeDimensionsRemainDistinctAfterNativeRebuildUpdateAndDelete()
        => AnalyticsBoundsDimensionsFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    public Task NativeDimensionEncodingRetainsFreshFieldUseAndRawReadIndependence()
        => AnalyticsBoundsPrivacyFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
