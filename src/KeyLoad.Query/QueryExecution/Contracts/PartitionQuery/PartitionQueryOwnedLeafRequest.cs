namespace KeyLoad.Query.Features.QueryExecution;

[GenerateSerializer, Alias(PartitionQuerySerializationAliases.OwnedLeafRequest)]
internal sealed record PartitionQueryOwnedLeafRequest(
    [property: Id(0)] PartitionQueryLeafPlanV1 Plan,
    [property: Id(1)] PhysicalShardRecord Owner,
    [property: Id(2)] string Tenant);
