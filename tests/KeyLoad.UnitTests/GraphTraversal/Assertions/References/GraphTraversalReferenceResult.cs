using System.Collections.Immutable;
namespace KeyLoad.UnitTests.GraphTraversal;

/// <summary>Reference membership, minimum depths, and candidate-work result.</summary>
internal sealed record GraphTraversalReferenceResult(
    ImmutableArray<GraphTraversalReferenceVertex> Vertices,
    ImmutableArray<GraphTraversalReferenceEdge> Edges,
    ImmutableDictionary<GraphTraversalReferenceVertex, int> ShortestDepths,
    int ExaminedEdges,
    bool BudgetExceeded);
