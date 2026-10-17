namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed class FeedDeleteClassificationWholeFlowTests
{
    [Test]
    public Task RetainedDeleteCannotBecomeLiveUpsertThenExactTombstoneRepairOriginalReceiptsAndFreshResurrectionRemainGapFreeAcrossTwoColdCuts()
        => FeedDeleteClassificationFlow.RunAsync();
}
