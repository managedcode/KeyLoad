namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed class FeedDeleteClassificationWholeFlowTests
{
    [Test]
    public Task RetainedDeleteCannotBecomeLiveUpsertThenExactTombstoneRepairOriginalReceiptsAndFreshResurrectionRemainGapFreeAcrossTwoColdCuts()
        => FeedDeleteClassificationFlow.RunAsync();
}
