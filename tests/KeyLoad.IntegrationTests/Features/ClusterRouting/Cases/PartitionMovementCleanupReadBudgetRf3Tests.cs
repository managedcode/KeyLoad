namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PartitionMovementCleanupReadBudgetRf3Tests
{
    [Test]
    public Task NativeCleanupReadRefusalRetainsOriginalFailureAcrossColdThenWholePartitionsSplitAndMerge()
        => PartitionMovementCleanupReadBudgetRf3Trial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
