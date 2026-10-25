namespace KeyLoad.FunctionalTests.GraphTraversal;

internal sealed record GraphShortestPathReferenceEdge(
    string Id,
    GraphShortestPathReferenceVertex From,
    GraphShortestPathReferenceVertex To,
    string Label,
    string AttributesJson = "{}");
