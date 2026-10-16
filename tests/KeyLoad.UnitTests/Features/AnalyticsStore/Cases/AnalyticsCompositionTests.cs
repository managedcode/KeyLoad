namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal sealed class AnalyticsCompositionTests
{
    [Test]
    public Task MixedModelAnalyticsBatchRollsBackReplaysRepairsPolicyAndSurvivesTwoNativeColdReopens()
        => AnalyticsCompositionFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
