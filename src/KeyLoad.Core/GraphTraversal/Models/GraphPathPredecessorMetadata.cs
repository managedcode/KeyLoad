namespace KeyLoad.Core.GraphTraversal;

/// <summary>Models retained predecessor identity for conservative admission.</summary>
internal sealed record GraphPathPredecessorMetadata(EntityRef Vertex, EntityRef Previous, string EdgeId);
