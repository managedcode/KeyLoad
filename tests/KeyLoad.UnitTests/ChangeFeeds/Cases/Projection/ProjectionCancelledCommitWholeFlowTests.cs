namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed class ProjectionCancelledCommitWholeFlowTests
{
    [Test]
    public Task OriginalCancelledProjectionGuidRetainsNoEffectThenSameGuidCommitsContiguouslyAcrossTwoColdCutsAndFreshHealthySource()
        => ProjectionCancelledCommitFlow.RunAsync();
}
