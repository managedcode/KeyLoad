namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

[NotInParallel]
internal sealed class StrongReadSessionAbsenceRf3Tests
{
    [Test]
    public Task MissingAndCommittedDeletedRowsStillRequireSessionAuthorityAfterFailoverPolicyRepairReplayAndTwoColdCuts()
        => StrongReadSessionFlow.RunAsync();
}
