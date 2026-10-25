namespace KeyLoad.ClusterTests.Search;

[NotInParallel]
internal sealed class GraphSearchLineageClusterTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task AllRequestedGraphLabelsUseFreshAuthorityBeforeEarlierLookupAndRecoverExactResultsAcrossTwoCold(bool expansion, bool emptyLabels)
        => GraphSearchLineageClusterFlow.RunAsync(expansion, emptyLabels);
}
