namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class QueueLifecycleIncrementCapacityWholeFlowTests
{
    [Test]
    [Arguments(QueueLifecycleCeilingFault.RedriveStateVersion)]
    [Arguments(QueueLifecycleCeilingFault.RedriveDeliveryGeneration)]
    [Arguments(QueueLifecycleCeilingFault.RedriveLeaseVersion)]
    [Arguments(QueueLifecycleCeilingFault.RedriveReadySequence)]
    [Arguments(QueueLifecycleCeilingFault.RedriveOrderSequence)]
    [Arguments(QueueLifecycleCeilingFault.CancelStateVersion)]
    [Arguments(QueueLifecycleCeilingFault.CancelDeliveryGeneration)]
    [Arguments(QueueLifecycleCeilingFault.CancelLeaseVersion)]
    [Arguments(QueueLifecycleCeilingFault.ParkStateVersion)]
    [Arguments(QueueLifecycleCeilingFault.ParkSequence)]
    public Task GenuineRetainedLifecycleCeilingKeepsImmutableFailureThenExactRepairFreshTransitionTwoColdAndHealthy(
        QueueLifecycleCeilingFault fault)
        => QueueLifecycleIncrementCapacityFlow.RunAsync(fault, TestContext.Current!.Execution.CancellationToken);
}
