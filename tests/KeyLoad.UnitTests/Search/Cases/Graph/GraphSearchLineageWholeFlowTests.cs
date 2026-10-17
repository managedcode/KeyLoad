namespace KeyLoad.UnitTests.Features.Search;

internal sealed class GraphSearchLineageWholeFlowTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task AllGraphExpressionAuthorityPrecedesEarlierNativeLookupThenUseOnlyRepairCompleteResultsReplayAndTwoCold(bool expansion, bool emptyLabels)
        => GraphSearchLineageFlow.RunAsync(expansion, emptyLabels);
}
