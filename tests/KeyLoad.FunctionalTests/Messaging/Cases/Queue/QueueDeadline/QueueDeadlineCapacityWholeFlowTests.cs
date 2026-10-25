namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class QueueDeadlineCapacityWholeFlowTests
{
    [Test]
    [Arguments(QueueDeadlineKind.ExpireLease, false, false, QueueDeadlineCapacityFault.ParkedSequence)]
    [Arguments(QueueDeadlineKind.ExpireLease, true, false, QueueDeadlineCapacityFault.ParkedSequence)]
    [Arguments(QueueDeadlineKind.PromoteScheduled, false, false, QueueDeadlineCapacityFault.StateVersion)]
    [Arguments(QueueDeadlineKind.PromoteScheduled, true, false, QueueDeadlineCapacityFault.StateVersion)]
    [Arguments(QueueDeadlineKind.ExpireLease, false, false, QueueDeadlineCapacityFault.StateVersion)]
    [Arguments(QueueDeadlineKind.ExpireLease, true, false, QueueDeadlineCapacityFault.StateVersion)]
    [Arguments(QueueDeadlineKind.ExpireMessage, false, false, QueueDeadlineCapacityFault.StateVersion)]
    [Arguments(QueueDeadlineKind.PromoteScheduled, false, false, QueueDeadlineCapacityFault.ReadySequence)]
    [Arguments(QueueDeadlineKind.PromoteScheduled, true, false, QueueDeadlineCapacityFault.ReadySequence)]
    [Arguments(QueueDeadlineKind.ExpireLease, false, false, QueueDeadlineCapacityFault.ReadySequence)]
    [Arguments(QueueDeadlineKind.ExpireLease, true, false, QueueDeadlineCapacityFault.ReadySequence)]
    [Arguments(QueueDeadlineKind.ExpireLease, false, true, QueueDeadlineCapacityFault.StateVersion)]
    [Arguments(QueueDeadlineKind.ExpireLease, true, true, QueueDeadlineCapacityFault.StateVersion)]
    public Task DeadlineCeilingRetainsCompleteInputFailedIdentityAndColdThenExactRepairFreshDecisionAndHealthyAck(
        QueueDeadlineKind kind, bool sweep, bool jitter, QueueDeadlineCapacityFault fault)
        => QueueDeadlineCapacityFlow.RunAsync(kind, sweep, jitter, fault, TestContext.Current!.Execution.CancellationToken);

    [Test]
    [Arguments(false, QueueDeadlineCapacityFault.FullDeadLetterMaxPark)]
    [Arguments(true, QueueDeadlineCapacityFault.FullDeadLetterMaxPark)]
    [Arguments(false, QueueDeadlineCapacityFault.ExpiryBeforeDeadLetterMaxPark)]
    [Arguments(true, QueueDeadlineCapacityFault.ExpiryBeforeDeadLetterMaxPark)]
    public Task FullDeadLetterAndExpiryPriorityDoNotConsumeExhaustedParkSequenceThenColdAndHealthyWork(
        bool sweep, QueueDeadlineCapacityFault control)
        => QueueDeadlineCapacityFlow.PositiveAsync(sweep, control, TestContext.Current!.Execution.CancellationToken);
}
