namespace KeyLoad.FunctionalTests.GraphTraversal;

internal sealed class GraphLabelUseWholeFlowTests
{
    [Test]
    public Task TraversalLabelUseRefusesBeforeVertexLookupThenFreshGrantRepairReplayAndTwoCold()
        => GraphLabelUseFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
