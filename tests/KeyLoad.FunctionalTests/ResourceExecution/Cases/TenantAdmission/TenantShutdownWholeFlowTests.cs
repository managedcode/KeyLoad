namespace KeyLoad.FunctionalTests.ResourceExecution;

internal sealed class TenantShutdownWholeFlowTests
{
    [Test]
    public async Task OwnerShutdownCancelsSelectedWorkThenOriginalCommandReplayAndSameRootColdPreserveOutcome()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await TenantShutdownWholeFlow.RunAsync(token);
    }
}
