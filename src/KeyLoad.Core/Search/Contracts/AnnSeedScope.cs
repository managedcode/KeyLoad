namespace KeyLoad.Core.Features.Search;

internal sealed record AnnSeedScope(string PrincipalId, long PolicyEpoch,
    PartitionRef Partition, string Collection, string Field, long SchemaVersion,
    VectorSpace Space, DateTimeOffset EvaluatedAt);
