namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PartitionMovementPolicyEpochColdRf3Tests
{
    [Test]
    public Task ActualDemotionDeniesParentThenRestoredAdminReplaysParentButNotOldUserReceiptsAndMovesCold()
        => PartitionMovementPolicyEpochColdRf3Trial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
