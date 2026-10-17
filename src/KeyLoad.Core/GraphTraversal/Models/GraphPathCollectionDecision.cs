namespace KeyLoad.Core.Features.GraphTraversal;

/// <summary>Models a retained collection authorization decision.</summary>
internal sealed record GraphPathCollectionDecision(PartitionRef Partition, string Collection);
