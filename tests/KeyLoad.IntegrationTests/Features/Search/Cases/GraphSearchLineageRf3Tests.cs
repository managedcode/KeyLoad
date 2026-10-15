namespace KeyLoad.IntegrationTests.Features.Search;

[NotInParallel]
internal sealed class GraphSearchLineageRf3Tests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task AllRequestedGraphLabelsUseFreshAuthorityBeforeEarlierLookupAndRecoverExactResultsAcrossTwoCold(bool expansion, bool emptyLabels)
        => GraphSearchLineageRf3Flow.RunAsync(expansion, emptyLabels);
}
