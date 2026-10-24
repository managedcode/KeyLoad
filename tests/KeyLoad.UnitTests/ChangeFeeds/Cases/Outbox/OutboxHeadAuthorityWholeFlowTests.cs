namespace KeyLoad.UnitTests.ChangeFeeds;

internal sealed class OutboxHeadAuthorityWholeFlowTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public Task CorruptRetainedHeadRefusesWholeProducerOrPurgeThenExactRepairAndOriginalReceiptsSurviveTwoColdCuts(int fault)
        => OutboxHeadAuthorityFlow.RunAsync(fault);
}
