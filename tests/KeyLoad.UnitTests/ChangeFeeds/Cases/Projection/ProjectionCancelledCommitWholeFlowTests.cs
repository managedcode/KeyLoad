namespace KeyLoad.UnitTests.ChangeFeeds;

internal sealed class ProjectionCancelledCommitWholeFlowTests
{
    [Test]
    public Task OriginalCancelledProjectionGuidRetainsNoEffectThenSameGuidCommitsContiguouslyAcrossTwoColdCutsAndFreshHealthySource()
        => ProjectionCancelledCommitFlow.RunAsync();
}
