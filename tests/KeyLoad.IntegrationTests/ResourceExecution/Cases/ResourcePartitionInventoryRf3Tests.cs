namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class ResourcePartitionInventoryRf3Tests
{
    [Test]
    public Task AuthenticatedConservativeInventoryRetainsReplayRollbackAndTwoColdOwners()
        => ResourcePartitionInventoryRf3Flow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
