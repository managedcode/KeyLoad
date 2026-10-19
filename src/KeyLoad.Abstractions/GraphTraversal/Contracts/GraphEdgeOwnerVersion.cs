namespace KeyLoad;

/// <summary>Persists the monotonic source-owned edge revision even after deletion.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.OwnerVersion)]
internal sealed record GraphEdgeOwnerVersion(
    [property: Orleans.Id(GraphCrossPartitionFields.OwnerVersion)] int Version,
    [property: Orleans.Id(GraphCrossPartitionFields.OwnerRevision)] long Revision,
    [property: Orleans.Id(GraphCrossPartitionFields.OwnerDeleted)] bool Deleted);
