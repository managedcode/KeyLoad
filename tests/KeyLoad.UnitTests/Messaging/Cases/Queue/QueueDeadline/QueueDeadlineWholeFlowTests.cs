namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueDeadlineWholeFlowTests
{
    [Test]
    [Arguments(QueueDeadlineKind.PromoteScheduled, false)]
    [Arguments(QueueDeadlineKind.PromoteScheduled, true)]
    [Arguments(QueueDeadlineKind.ExpireLease, false)]
    [Arguments(QueueDeadlineKind.ExpireLease, true)]
    public Task ForeignDueBodyIdentityRefusesWholeOperationThenExactRepairColdReplayAndHealthyAck(
        QueueDeadlineKind kind, bool receiveSweep)
        => QueueDeadlineTrial.CorruptBodyAsync(kind, receiveSweep, TestContext.Current!.Execution.CancellationToken);

    [Test]
    [Arguments(QueueDeadlineKind.PromoteScheduled)]
    [Arguments(QueueDeadlineKind.ExpireLease)]
    [Arguments(QueueDeadlineKind.ExpireMessage)]
    public async Task ActualDeadlineCasRefusesNotDueStaleAndRevokedThenAdvancesReplaysAcrossTwoColdOwnersAndHealthyWork(
        QueueDeadlineKind kind)
        => await QueueDeadlineTrial.RunAsync(kind, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public async Task ActualSignedLeaseRetryDeadlineRetainsChosenTimeAcrossColdFencesOldTokenThenPromotesAndHealthyWork()
        => await QueueDeadlineSignedRetry.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
