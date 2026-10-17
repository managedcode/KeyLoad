namespace KeyLoad.RecoveryTests.Features.AnalyticsStore;

internal sealed class UserAnalyticsDerivedProcessRecoveryTests
{
    [Test]
    public async Task KilledRealNativeGenerationReopensSamePhysicalRootPreservingCanonicalReceiptAndHealthyAnalytics()
        => await UserAnalyticsDerivedProcessTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
