namespace KeyLoad.IntegrationTests.QueryExecution;

[NotInParallel]
internal sealed class QueueViewLeaseIsolationTests
{
    [Test]
    public Task ActiveLeaseViewsPreserveClaimsDuringSqlAstCurrentPolicyRepairThenOriginalTokenRenewAckAndTwoColdCuts()
        => QueueViewLeaseIsolationFlow.RunAsync();
}
