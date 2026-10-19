namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal sealed class AnalyticsCompositionTests
{
    [Test]
    public Task MixedModelAnalyticsBatchRollsBackReplaysRepairsPolicyAndSurvivesTwoColdReopens()
        => AnalyticsCompositionFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
