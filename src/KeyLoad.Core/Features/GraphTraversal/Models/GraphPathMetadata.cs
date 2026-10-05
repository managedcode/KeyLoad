namespace KeyLoad.Core.Features.GraphTraversal;

/// <summary>Stores only the first predecessor needed to reconstruct a discovered path.</summary>
internal sealed record GraphPathPredecessor(EntityRef Previous, string EdgeId);

/// <summary>Models retained predecessor identity for conservative admission.</summary>
internal sealed record GraphPathPredecessorMetadata(EntityRef Vertex, EntityRef Previous, string EdgeId);

/// <summary>Models a retained collection authorization decision.</summary>
internal sealed record GraphPathCollectionDecision(PartitionRef Partition, string Collection);
