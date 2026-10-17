namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class RelationalQueryBindingWholeFlowTests
{
    [Test]
    public Task TypedPredicatesBindBeforeEmptyOrPopulatedScanThenPolicyRepairReplayAndTwoCold()
        => RelationalQueryBindingFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
