namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueueViewLeaseIsolationTests
{
    [Test]
    public Task ActiveLeaseViewsArePureMaskedCurrentAuthorityAndOriginalHistorySurvivesRenewAckHealthyAndTwoCold()
        => QueueViewLeaseIsolationFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
