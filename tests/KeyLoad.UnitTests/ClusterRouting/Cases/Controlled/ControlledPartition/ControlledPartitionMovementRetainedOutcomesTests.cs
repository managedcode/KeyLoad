namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ControlledPartitionMovementRetainedOutcomesTests
{
    [Test]
    public Task AllEightSeedOutcomesRemainSourceOwnedThroughTerminalWorkAndTwoColdCuts()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(
            ControlledPartitionMovementTerminalTrial.ExecuteRetainedOutcomesAsync);
}
