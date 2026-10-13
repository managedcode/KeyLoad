namespace KeyLoad.UnitTests.Features.ChangeFeeds;

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
}
