namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed class ProjectionEmptyTailWholeFlowTests
{
    [Test]
    public Task OriginalEmptyTailCannotPublishLaterEffectsOrAdvanceNewSourceAndFullHistoricalResultsSurviveHealthyContiguousTwoColdContinuation()
        => ProjectionEmptyTailFlow.RunAsync();
}
