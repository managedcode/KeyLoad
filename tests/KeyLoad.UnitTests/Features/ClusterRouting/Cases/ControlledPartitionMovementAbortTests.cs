namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ControlledPartitionMovementAbortTests
{
    [Test]
    public Task GenuineActiveCaptureAbortJoinsBothOwnersPreservesOriginalModelAndColdHealthyCommand()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(ControlledPartitionMovementAbortTrial.ExecuteAsync);
}
