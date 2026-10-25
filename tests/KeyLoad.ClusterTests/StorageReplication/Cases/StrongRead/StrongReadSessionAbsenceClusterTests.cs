namespace KeyLoad.ClusterTests.StorageReplication;

[NotInParallel]
internal sealed class StrongReadSessionAbsenceClusterTests
{
    [Test]
    public Task MissingAndCommittedDeletedRowsStillRequireSessionAuthorityAfterFailoverPolicyRepairReplayAndTwoColdCuts()
        => StrongReadSessionFlow.RunAsync();
}
