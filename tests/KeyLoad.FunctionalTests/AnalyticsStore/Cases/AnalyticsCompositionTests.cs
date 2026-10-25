namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal sealed class AnalyticsCompositionTests
{
    [Test]
    public Task MixedModelAnalyticsBatchRollsBackReplaysRepairsPolicyAndSurvivesTwoColdReopens()
        => AnalyticsCompositionFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
