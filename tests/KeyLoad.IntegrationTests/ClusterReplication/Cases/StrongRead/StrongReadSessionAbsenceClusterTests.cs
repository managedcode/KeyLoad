namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

[NotInParallel]
internal sealed class StrongReadSessionAbsenceClusterTests
{
    [Test]
    public Task MissingAndCommittedDeletedRowsStillRequireSessionAuthorityAfterFailoverPolicyRepairReplayAndTwoColdCuts()
        => StrongReadSessionFlow.RunAsync();
}
