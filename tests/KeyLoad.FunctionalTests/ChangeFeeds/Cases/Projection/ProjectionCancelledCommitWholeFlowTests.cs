namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed class ProjectionCancelledCommitWholeFlowTests
{
    [Test]
    public Task OriginalCancelledProjectionGuidRetainsNoEffectThenSameGuidCommitsContiguouslyAcrossTwoColdCutsAndFreshHealthySource()
        => ProjectionCancelledCommitFlow.RunAsync();
}
