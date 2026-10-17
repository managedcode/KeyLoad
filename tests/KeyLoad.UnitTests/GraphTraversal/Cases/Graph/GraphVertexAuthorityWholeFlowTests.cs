namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphVertexAuthorityWholeFlowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task CanonicalVertexIdentityRefusesTraversalAndAtomicEdgeWritesThenExactRepairSurvivesTwoColdCuts(bool start)
        => GraphVertexAuthorityFlow.Run(start);
}
