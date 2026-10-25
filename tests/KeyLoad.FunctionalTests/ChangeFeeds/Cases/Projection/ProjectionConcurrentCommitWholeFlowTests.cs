namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed class ProjectionConcurrentCommitWholeFlowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task CompetingCommandGuidsForOneSignedBatchRetainOneCompleteEffectAndMatchingReplayOrOriginalConflictAcrossContiguousHealthyAdvanceAndTwoColdCuts(bool changed)
        => ProjectionConcurrentCommitFlow.RunAsync(changed);
}
