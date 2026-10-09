namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PartitionMovementParentCapacityRf3Tests
{
    [Test]
    public Task ActualCapturedStageShapeBoundAndConfiguredGrantDenialRetainAuthorityThenRestoreHealthyMove()
        => PartitionMovementParentCapacityRf3Trial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
