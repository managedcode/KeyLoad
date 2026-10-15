namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PhysicalShardCleanupRestartRf3Tests
{
    [Test]
    public Task OriginalCommittedSourceCleanupPageResumesSameWholePartitionPlanThenMergeCold()
        => PhysicalShardCleanupRestartRf3Trial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
