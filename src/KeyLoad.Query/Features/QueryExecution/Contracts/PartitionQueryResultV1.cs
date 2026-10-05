using System.Collections.Immutable;

namespace KeyLoad.Query.Features.QueryExecution;

[GenerateSerializer]
[Alias(PartitionQuerySerializationAliases.Result)]
internal sealed record PartitionQueryResultV1(
    [property: Id(0)] int Version,
    [property: Id(1)] bool Complete,
    [property: Id(2)] ImmutableArray<PartitionQueryLeafResultV1> Leaves,
    [property: Id(3)] ImmutableArray<PartitionQueryCandidateV1> Rows,
    [property: Id(4)] int ExaminedRecords,
    [property: Id(5)] long ReadBytes,
    [property: Id(6)] long RetainedBytes);
