namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PartitionMovementPolicyBusyColdRf3Tests
{
    [Test]
    public Task ActualUnsettledMoveRejectsPolicyChangeThenColdAbortAndFreshMoveSdkMcpQ1Healthy()
        => PartitionMovementFinalInstallFrameRf3Trial.RunPolicyBusyColdAsync(
            TestContext.Current!.Execution.CancellationToken);
}
