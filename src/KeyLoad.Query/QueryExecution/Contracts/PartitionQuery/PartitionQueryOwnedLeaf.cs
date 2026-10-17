namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Authenticates one completed native leaf to its admitted physical destination.</summary>
internal sealed record PartitionQueryOwnedLeaf(PhysicalShardRecord Owner, PartitionQueryLeafResultV1 Result);
