using System.Collections.Immutable;

namespace KeyLoad.FunctionalTests.GraphTraversal;

internal sealed record GraphShortestPathReference(
    bool Found,
    ImmutableArray<GraphShortestPathReferenceVertex> Vertices,
    ImmutableArray<GraphShortestPathReferenceEdge> Edges,
    int ExaminedEdges);
