namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ControlledPartitionMovementTerminalTests
{
    [Test]
    public Task GenuineInstalledPublicationRetiresSourcePreservesOriginalReceiptAndBothColdOwners()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(ControlledPartitionMovementTerminalTrial.ExecuteAsync);
}
