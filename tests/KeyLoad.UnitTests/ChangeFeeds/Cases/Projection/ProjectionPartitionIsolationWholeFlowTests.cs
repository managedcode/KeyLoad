namespace KeyLoad.UnitTests.ChangeFeeds;

internal sealed class ProjectionPartitionIsolationWholeFlowTests
{
    [Test]
    public Task SameNamedPartitionConsumersRejectCrossScopeEffectsKeepIndependentPinsAndReplayOriginalFailuresThenReclaimAndContinueAfterTwoColdCuts()
        => ProjectionPartitionIsolationFlow.RunAsync();
}
