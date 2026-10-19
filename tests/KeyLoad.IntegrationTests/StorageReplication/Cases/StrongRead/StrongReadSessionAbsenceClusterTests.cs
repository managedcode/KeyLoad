namespace KeyLoad.IntegrationTests.Features.StorageReplication;

[NotInParallel]
internal sealed class StrongReadSessionAbsenceClusterTests
{
    [Test]
    public Task MissingAndCommittedDeletedRowsStillRequireSessionAuthorityAfterFailoverPolicyRepairReplayAndTwoColdCuts()
        => StrongReadSessionFlow.RunAsync();
}
