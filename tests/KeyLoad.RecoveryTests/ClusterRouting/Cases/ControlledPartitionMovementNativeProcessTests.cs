namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ControlledPartitionMovementNativeProcessTests
{
    [Test]
    public Task GenuineSixHostOriginalPrepareSurvivesKilledOwnerAndColdOutcomeObservationWithoutRedispatch()
        => ControlledPartitionMovementNativeProcessOperation.RunAsync(
            RecoveryExecutionOptions.NativeProcessReadiness(), TimeProvider.System,
            TestContext.Current!.Execution.CancellationToken);
}
