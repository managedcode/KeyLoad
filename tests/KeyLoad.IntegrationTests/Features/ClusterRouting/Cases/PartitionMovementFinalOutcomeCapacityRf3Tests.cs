namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PartitionMovementFinalOutcomeCapacityRf3Tests
{
    [Test]
    public Task ActualFinalInstallOutcomeCapacityLegalAndOneByteShortThenRealFailedObserveAbortAndColdHealthy()
        => PartitionMovementFinalOutcomeCapacityRf3Trial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
