namespace KeyLoad.UnitTests.Messaging;

internal sealed class QueueLifecycleNonIncrementWholeFlowTests
{
    [Test]
    [Arguments(QueueLifecycleNonIncrementControl.BlockRedriveKeepsOrder)]
    [Arguments(QueueLifecycleNonIncrementControl.ScheduledRedriveKeepsReady)]
    [Arguments(QueueLifecycleNonIncrementControl.FullDeadLetterKeepsParked)]
    public Task AcMsg088MaximumUnusedSequencePreservesSuccessTwoColdAndHealthy(
        QueueLifecycleNonIncrementControl control)
        => control == QueueLifecycleNonIncrementControl.FullDeadLetterKeepsParked
            ? QueueFullDeadLetterSequenceControl.RunAsync(TestContext.Current!.Execution.CancellationToken)
            : QueueRedriveUnusedSequenceControl.RunAsync(control, TestContext.Current!.Execution.CancellationToken);
}
