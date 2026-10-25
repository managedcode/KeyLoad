namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed class ProjectionCheckpointRetentionWholeFlowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task PersistedCheckpointOutsideRetainedRangeRefusesAtomicPurgeThenExactRepairContiguousEffectsReclaimOriginalFailuresAndHealthySourceSurviveTwoColdCuts(bool ahead)
        => ProjectionCheckpointRetentionFlow.RunAsync(ahead);
}
