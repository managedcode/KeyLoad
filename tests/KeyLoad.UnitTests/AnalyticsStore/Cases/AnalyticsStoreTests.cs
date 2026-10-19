namespace KeyLoad.UnitTests.Features.AnalyticsStore;

/// <summary>Actual canonical ordered execution apply plus disposable execution analytics; not public CLUSTER qualification.</summary>
internal sealed class AnalyticsStoreTests
{
    [Test]
    public async Task CanonicalCasMutationsRebuildCompleteAggregatesAndReplayOriginalReceipt()
        => await AnalyticsStoreMutationFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public async Task CurrentRowsAndFieldUseAreIndependentFromRawReadAndRepairedByPersistedEpoch()
        => await AnalyticsStorePrivacyFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public async Task ActualSourceMutationInvalidatesCapturedOutputAndMalformedMetricNeverOpensProjection()
        => await AnalyticsStoreCutFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
