namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed class OutboxDocumentMutationWholeFlowTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public Task ContradictoryRetainedDocumentMutationRefusesWholeProjectionAndFeedBeforeFilteringThenExactRepairOriginalEffectsAndReceiptsSurviveTwoColdCuts(int fault)
        => OutboxDocumentMutationFlow.RunAsync(fault);

    [Test]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public Task ContradictoryRetainedDocumentRevisionOrMissingImageRefusesWholeFeedAndProjectionThenOriginalEffectsAndReceiptsSurviveExactRepairAndTwoColdCuts(int fault)
        => OutboxDocumentMutationFlow.RunAsync(fault);
}
