namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed class ProjectionPartitionIsolationWholeFlowTests
{
    [Test]
    public Task SameNamedPartitionConsumersRejectCrossScopeEffectsKeepIndependentPinsAndReplayOriginalFailuresThenReclaimAndContinueAfterTwoColdCuts()
        => ProjectionPartitionIsolationFlow.RunAsync();
}
