namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed class FeedVertexAuthorityWholeFlowTests
{
    [Test]
    public Task ForeignCurrentDocumentAuthorityRefusesWholeFeedAndLiveTailThenExactRepairReceiptsSurviveTwoColdCuts()
        => FeedVertexAuthorityFlow.RunAsync();

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public Task HistoricalDocumentIdentityRefusesWholeFeedAndLiveTailThenRawEntryRepairReceiptsSurviveTwoColdCuts(int fault)
        => FeedVertexAuthorityFlow.RunAsync(fault);

    [Test]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public Task ContradictoryHistoricalRevisionChainRefusesWholeFeedAndLiveTailThenExactRawRepairOriginalReceiptsAndHealthyContinuationSurviveTwoColdCuts(int fault)
        => FeedVertexAuthorityFlow.RunAsync(fault);
}
