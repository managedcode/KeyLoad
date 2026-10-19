namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Authenticates one completed execution leaf to its admitted physical destination.</summary>
internal sealed record PartitionQueryOwnedLeaf(PhysicalShardRecord Owner, PartitionQueryLeafResult Result);
