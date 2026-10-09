namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PartitionMovementFrameObservationScopeRf3Tests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualUnselectedOrForeignReceiverObservationCannotPublishProofThenSameMoveColdHealthy(bool foreignMove)
        => PartitionMovementFinalInstallFrameRf3Trial.RunUnselectedAsync(foreignMove,
            TestContext.Current!.Execution.CancellationToken);
}
