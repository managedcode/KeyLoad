namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphShortestPathSelectedIdentityWholeFlowTests
{
    [Test]
    [Arguments(GraphTraversalIntegrityFault.MissingEdge, false)]
    [Arguments(GraphTraversalIntegrityFault.MissingEdge, true)]
    [Arguments(GraphTraversalIntegrityFault.EdgeIdentity, false)]
    [Arguments(GraphTraversalIntegrityFault.EdgeIdentity, true)]
    [Arguments(GraphTraversalIntegrityFault.EdgeSource, false)]
    [Arguments(GraphTraversalIntegrityFault.EdgeSource, true)]
    [Arguments(GraphTraversalIntegrityFault.AdjacencyIdentity, false)]
    [Arguments(GraphTraversalIntegrityFault.AdjacencyIdentity, true)]
    [Arguments(GraphTraversalIntegrityFault.InvalidLabel, false)]
    [Arguments(GraphTraversalIntegrityFault.InvalidLabel, true)]
    [Arguments(GraphTraversalIntegrityFault.InvalidTargetId, false)]
    [Arguments(GraphTraversalIntegrityFault.InvalidTargetId, true)]
    [Arguments(GraphTraversalIntegrityFault.NullTarget, false)]
    [Arguments(GraphTraversalIntegrityFault.NullTarget, true)]
    [Arguments(GraphTraversalIntegrityFault.MalformedAdjacency, false)]
    [Arguments(GraphTraversalIntegrityFault.MalformedAdjacency, true)]
    [Arguments(GraphTraversalIntegrityFault.WrongAdjacencyType, false)]
    [Arguments(GraphTraversalIntegrityFault.WrongAdjacencyType, true)]
    public Task AcGraphPathIdentity001SelectedDamageRefusesThenSameRepairReplayAndTwoColdCuts(
        GraphTraversalIntegrityFault fault, bool filtered)
        => GraphShortestPathSelectedIdentityFlow.Run(fault, filtered);
}
