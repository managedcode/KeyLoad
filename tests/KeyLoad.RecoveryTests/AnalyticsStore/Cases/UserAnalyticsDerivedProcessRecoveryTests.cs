namespace KeyLoad.RecoveryTests.AnalyticsStore;

internal sealed class UserAnalyticsDerivedProcessRecoveryTests
{
    [Test]
    public async Task KilledRealGenerationReopensSamePhysicalRootPreservingCanonicalReceiptAndHealthyAnalytics()
        => await UserAnalyticsDerivedProcessTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
