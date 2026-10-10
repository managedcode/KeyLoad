namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueDeadlineNativeWholeFlowTests
{
    [Test]
    [Arguments(QueueDeadlineKind.PromoteScheduled)]
    [Arguments(QueueDeadlineKind.ExpireLease)]
    [Arguments(QueueDeadlineKind.ExpireMessage)]
    public async Task ActualDeadlineCasRefusesNotDueStaleAndRevokedThenAdvancesReplaysAcrossTwoColdOwnersAndHealthyWork(
        QueueDeadlineKind kind)
        => await QueueDeadlineNativeTrial.RunAsync(kind, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public async Task ActualSignedLeaseRetryDeadlineRetainsChosenTimeAcrossColdFencesOldTokenThenPromotesAndHealthyWork()
        => await QueueDeadlineSignedRetry.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
