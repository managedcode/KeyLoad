using System.Collections.Immutable;

namespace KeyLoad.Query.Features.QueryExecution;

[GenerateSerializer]
[Alias(PartitionQuerySerializationAliases.Plan)]
internal sealed record PartitionQueryPlanV1(
    [property: Id(0)] int Version,
    [property: Id(1)] Guid NodeId,
    [property: Id(2)] Guid Incarnation,
    [property: Id(3)] long ReadGeneration,
    [property: Id(4)] ImmutableArray<PartitionQueryLeafPlanV1> Leaves,
    [property: Id(5)] int Limit,
    [property: Id(6)] int MaxExaminedRecords,
    [property: Id(7)] long MaxReadBytes,
    [property: Id(8)] long MaxRetainedBytes);
