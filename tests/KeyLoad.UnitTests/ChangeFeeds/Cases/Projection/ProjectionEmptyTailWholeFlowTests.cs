namespace KeyLoad.UnitTests.ChangeFeeds;

internal sealed class ProjectionEmptyTailWholeFlowTests
{
    [Test]
    public Task OriginalEmptyTailCannotPublishLaterEffectsOrAdvanceNewSourceAndFullHistoricalResultsSurviveHealthyContiguousTwoColdContinuation()
        => ProjectionEmptyTailFlow.RunAsync();
}
