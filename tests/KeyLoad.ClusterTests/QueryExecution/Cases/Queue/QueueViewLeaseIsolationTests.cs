namespace KeyLoad.ClusterTests.QueryExecution;

[NotInParallel]
internal sealed class QueueViewLeaseIsolationTests
{
    [Test]
    public Task ActiveLeaseViewsPreserveClaimsDuringSqlAstCurrentPolicyRepairThenOriginalTokenRenewAckAndTwoColdCuts()
        => QueueViewLeaseIsolationFlow.RunAsync();
}
