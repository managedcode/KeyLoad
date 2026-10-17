namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ControlledPartitionMovementNativeHostLifetimeTests
{
    [Test]
    public async Task CanceledOriginalStopJoinsAllSixOwnersThenOriginalColdOutcomeAndHealthyReplayRemainExact()
        => await ControlledPartitionMovementNativeHostLifetimeFlow.RunAsync(
            RecoveryExecutionOptions.NativeProcessReadiness(), TimeProvider.System,
            TestContext.Current!.Execution.CancellationToken);
}
