namespace KeyLoad.Core.GraphTraversal;

/// <summary>Stores only the first predecessor needed to reconstruct a discovered path.</summary>
internal sealed record GraphPathPredecessor(EntityRef Previous, string EdgeId);
