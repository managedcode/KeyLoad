namespace KeyLoad.UnitTests.ChangeFeeds;

internal sealed class FeedDeleteClassificationWholeFlowTests
{
    [Test]
    public Task RetainedDeleteCannotBecomeLiveUpsertThenExactTombstoneRepairOriginalReceiptsAndFreshResurrectionRemainGapFreeAcrossTwoColdCuts()
        => FeedDeleteClassificationFlow.RunAsync();
}
