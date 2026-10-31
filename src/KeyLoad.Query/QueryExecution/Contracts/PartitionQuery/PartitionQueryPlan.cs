using System.Collections.Immutable;

namespace KeyLoad.Query.QueryExecution;

[GenerateSerializer]
[Alias(PartitionQuerySerializationAliases.Plan)]
internal sealed record PartitionQueryPlan(
    [property: Id(0)] int Version,
    [property: Id(1)] Guid NodeId,
    [property: Id(2)] Guid Incarnation,
    [property: Id(3)] long ReadGeneration,
    [property: Id(4)] ImmutableArray<PartitionQueryLeafPlan> Leaves,
    [property: Id(5)] int Limit);
