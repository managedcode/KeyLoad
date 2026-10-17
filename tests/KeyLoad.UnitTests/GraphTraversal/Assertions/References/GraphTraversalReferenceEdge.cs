namespace KeyLoad.UnitTests.Features.GraphTraversal;

/// <summary>Independent directed edge input for bounded reachability expectations.</summary>
internal sealed record GraphTraversalReferenceEdge(
    string Id,
    GraphTraversalReferenceVertex From,
    GraphTraversalReferenceVertex To,
    string Label);
