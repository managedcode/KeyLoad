using System.Collections.Immutable;

namespace KeyLoad.UnitTests.GraphTraversal;

internal sealed record GraphShortestPathReference(
    bool Found,
    ImmutableArray<GraphShortestPathReferenceVertex> Vertices,
    ImmutableArray<GraphShortestPathReferenceEdge> Edges,
    int ExaminedEdges);
