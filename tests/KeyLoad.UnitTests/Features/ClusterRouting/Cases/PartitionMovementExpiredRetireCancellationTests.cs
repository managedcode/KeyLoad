namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PartitionMovementExpiredRetireCancellationTests
{
    [Test]
    public Task CallerStampedCancellationAuthorityIsDeniedBeforeEffectsThenColdActualPrepareIsHealthy()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(PartitionMovementExpiredRetireCancellationTrial.CallerAuthorityAsync);

    [Test]
    public Task ActualPendingPrepareCannotUseDedicatedExpiredRetireCancellationThenColdPrepareIsHealthy()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(PartitionMovementExpiredRetireCancellationTrial.PrepareScopeAsync);
}
