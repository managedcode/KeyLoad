namespace KeyLoad.FunctionalTests.Search;

internal sealed class GraphSearchLineageWholeFlowTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task AllGraphExpressionAuthorityPrecedesEarlierLookupThenUseOnlyRepairCompleteResultsReplayAndTwoCold(bool expansion, bool emptyLabels)
        => GraphSearchLineageFlow.RunAsync(expansion, emptyLabels);
}
