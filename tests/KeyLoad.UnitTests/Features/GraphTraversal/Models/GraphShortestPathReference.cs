using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal readonly record struct GraphShortestPathReferenceVertex(string Collection, string Id);

internal sealed record GraphShortestPathReferenceEdge(
    string Id,
    GraphShortestPathReferenceVertex From,
    GraphShortestPathReferenceVertex To,
    string Label,
    string AttributesJson = "{}");

internal sealed record GraphShortestPathReference(
    bool Found,
    ImmutableArray<GraphShortestPathReferenceVertex> Vertices,
    ImmutableArray<GraphShortestPathReferenceEdge> Edges,
    int ExaminedEdges);
