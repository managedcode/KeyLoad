namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ControlledPartitionMovementTerminalTests
{
    [Test]
    public Task GenuineInstalledPublicationRetiresSourcePreservesOriginalReceiptAndBothColdOwners()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(ControlledPartitionMovementTerminalTrial.ExecuteAsync);
    [Test]
    public Task ActualIssuedRetireGrantExpiresWithoutMutationThenFreshAckAndFamiliesCompleteBothColdOwners()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(ControlledPartitionMovementTerminalTrial.ExecuteNaturalExpiryAsync);
}
