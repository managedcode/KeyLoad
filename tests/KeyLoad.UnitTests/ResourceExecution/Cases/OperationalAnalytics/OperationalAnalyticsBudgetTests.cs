namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class OperationalAnalyticsBudgetTests
{
    [Test]
    [NotInParallel]
    public Task ActualNativeReportByteRefusalLatchesOriginalFailureThenJoinedEngineReopensHealthyAndTwoColdConserveCanonicalState()
        => OperationalAnalyticsBudgetFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
