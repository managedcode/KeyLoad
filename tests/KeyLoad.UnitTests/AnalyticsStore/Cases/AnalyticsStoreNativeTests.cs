namespace KeyLoad.UnitTests.Features.AnalyticsStore;

/// <summary>Actual canonical ordered native apply plus disposable native analytics; not public RF3 qualification.</summary>
internal sealed class AnalyticsStoreNativeTests
{
    [Test]
    public async Task CanonicalCasMutationsRebuildCompleteNativeAggregatesAndReplayOriginalReceipt()
        => await AnalyticsStoreNativeMutationFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public async Task CurrentRowsAndFieldUseAreIndependentFromRawReadAndRepairedByPersistedEpoch()
        => await AnalyticsStoreNativePrivacyFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public async Task ActualSourceMutationInvalidatesCapturedNativeOutputAndMalformedMetricNeverOpensProjection()
        => await AnalyticsStoreNativeCutFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
