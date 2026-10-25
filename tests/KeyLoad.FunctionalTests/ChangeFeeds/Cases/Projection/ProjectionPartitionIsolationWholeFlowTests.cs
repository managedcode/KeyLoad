namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed class ProjectionPartitionIsolationWholeFlowTests
{
    [Test]
    public Task SameNamedPartitionConsumersRejectCrossScopeEffectsKeepIndependentPinsAndReplayOriginalFailuresThenReclaimAndContinueAfterTwoColdCuts()
        => ProjectionPartitionIsolationFlow.RunAsync();
}
