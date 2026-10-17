namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Actual authenticated original transfer data refuses a tighter cap before any forward effect.</summary>
[NotInParallel]
internal sealed class PartitionMovementRetainedPageCapacityRf3Tests
{
    [Test]
    public Task ActualOriginalRetainedPageExceedsCurrentCapWithoutEffectsThenSameMoveColdSdkMcpQ1IsHealthy()
        => PartitionMovementRetainedPageCapacityRf3Trial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
