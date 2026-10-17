namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed class ProjectionEmptyTailWholeFlowTests
{
    [Test]
    public Task OriginalEmptyTailCannotPublishLaterEffectsOrAdvanceNewSourceAndFullHistoricalResultsSurviveHealthyContiguousTwoColdContinuation()
        => ProjectionEmptyTailFlow.RunAsync();
}
