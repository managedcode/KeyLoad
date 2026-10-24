using System.Collections.Immutable;

namespace KeyLoad.Query.QueryExecution;

[GenerateSerializer]
[Alias(PartitionQuerySerializationAliases.Result)]
internal sealed record PartitionQueryResult(
    [property: Id(0)] int Version,
    [property: Id(1)] bool Complete,
    [property: Id(2)] ImmutableArray<PartitionQueryLeafResult> Leaves,
    [property: Id(3)] ImmutableArray<PartitionQueryCandidate> Rows,
    [property: Id(4)] int ExaminedRecords,
    [property: Id(5)] long ReadBytes,
    [property: Id(6)] long RetainedBytes);
