namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PartitionMovementObservedFailureColdRf3Tests
{
    [Test]
    public Task ActualObservedFailedInstallColdResumeRetainsNativeFailureThenAbortAndFreshMoveSdkMcpQ1Healthy()
        => PartitionMovementFinalInstallFrameRf3Trial.RunObservedFailureColdAsync(
            TestContext.Current!.Execution.CancellationToken);
}
