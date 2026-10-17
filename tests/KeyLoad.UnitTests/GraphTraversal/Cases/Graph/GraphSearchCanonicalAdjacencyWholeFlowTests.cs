namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphSearchCanonicalAdjacencyWholeFlowTests
{
    [Test]
    [Arguments(GraphSearchIntegrityOperator.Scope, GraphTraversalIntegrityFault.AdjacencyIdentity, false)]
    [Arguments(GraphSearchIntegrityOperator.Scope, GraphTraversalIntegrityFault.AdjacencyIdentity, true)]
    [Arguments(GraphSearchIntegrityOperator.Scope, GraphTraversalIntegrityFault.InvalidAttributes, false)]
    [Arguments(GraphSearchIntegrityOperator.Scope, GraphTraversalIntegrityFault.InvalidAttributes, true)]
    [Arguments(GraphSearchIntegrityOperator.Scope, GraphTraversalIntegrityFault.NonpositiveRevision, false)]
    [Arguments(GraphSearchIntegrityOperator.Scope, GraphTraversalIntegrityFault.NonpositiveRevision, true)]
    [Arguments(GraphSearchIntegrityOperator.Retriever, GraphTraversalIntegrityFault.AdjacencyIdentity, false)]
    [Arguments(GraphSearchIntegrityOperator.Retriever, GraphTraversalIntegrityFault.AdjacencyIdentity, true)]
    [Arguments(GraphSearchIntegrityOperator.Retriever, GraphTraversalIntegrityFault.InvalidAttributes, false)]
    [Arguments(GraphSearchIntegrityOperator.Retriever, GraphTraversalIntegrityFault.InvalidAttributes, true)]
    [Arguments(GraphSearchIntegrityOperator.Retriever, GraphTraversalIntegrityFault.NonpositiveRevision, false)]
    [Arguments(GraphSearchIntegrityOperator.Retriever, GraphTraversalIntegrityFault.NonpositiveRevision, true)]
    [Arguments(GraphSearchIntegrityOperator.Expansion, GraphTraversalIntegrityFault.AdjacencyIdentity, false)]
    [Arguments(GraphSearchIntegrityOperator.Expansion, GraphTraversalIntegrityFault.AdjacencyIdentity, true)]
    [Arguments(GraphSearchIntegrityOperator.Expansion, GraphTraversalIntegrityFault.InvalidAttributes, false)]
    [Arguments(GraphSearchIntegrityOperator.Expansion, GraphTraversalIntegrityFault.InvalidAttributes, true)]
    [Arguments(GraphSearchIntegrityOperator.Expansion, GraphTraversalIntegrityFault.NonpositiveRevision, false)]
    [Arguments(GraphSearchIntegrityOperator.Expansion, GraphTraversalIntegrityFault.NonpositiveRevision, true)]
    public Task CanonicalGraphOperatorsRejectDamagedAdjacencyBeforeFilteringThenRepairReplayHealthyAndTwoCold(
        GraphSearchIntegrityOperator selected, GraphTraversalIntegrityFault fault, bool filtered)
        => GraphSearchIntegrityFlow.RunAsync(selected, fault, filtered);
}
