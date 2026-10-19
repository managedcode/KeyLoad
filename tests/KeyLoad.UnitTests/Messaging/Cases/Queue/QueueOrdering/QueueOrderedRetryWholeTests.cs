namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueOrderedRetryWholeTests
{
    [Test]
    [Arguments(QueueParkedHeadPolicy.Continue)]
    [Arguments(QueueParkedHeadPolicy.Block)]
    public Task StrictHeadSignedRetryParkRedriveNegativeHealthyAndSameRootCold(QueueParkedHeadPolicy parkedHead)
        => QueueOrderedRetryTrial.RunAsync(parkedHead, TestContext.Current!.Execution.CancellationToken);

    [Test]
    [Arguments(MessageState.Scheduled)]
    [Arguments(MessageState.Leased)]
    public Task AcMsg089ForeignEarlierStrictHeadBodyRefusesFollowerReceiveThenExactRepairAckTwoColdAndHealthy(
        MessageState headState)
        => QueueStrictHeadBodyWholeFlow.RunAsync(headState, TestContext.Current!.Execution.CancellationToken);
}
