namespace KeyLoad.UnitTests.Messaging;

internal sealed class QueueLifecyclePhaseOneTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task FullDeadLetterPendingAtomicRefusalRedriveCancellationAndSameRootCold(bool byteSublimit)
        => QueueLifecycleTrial.RunAsync(byteSublimit, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task ExpiredRetainedDeadLetterAndPendingRefuseRedrivePromotionRollbackThenTwoColdExplicitCancelAndHealthy()
        => QueueExpiredDlqTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
