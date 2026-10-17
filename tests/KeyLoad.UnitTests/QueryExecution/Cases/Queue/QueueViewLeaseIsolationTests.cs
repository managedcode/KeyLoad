namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueueViewLeaseIsolationTests
{
    [Test]
    public Task ActiveLeaseViewsArePureMaskedCurrentAuthorityAndOriginalHistorySurvivesRenewAckHealthyAndTwoNativeCold()
        => QueueViewLeaseIsolationFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
