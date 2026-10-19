using KeyLoad.Core.Features.StoragePartitions.Contracts;

namespace KeyLoad.Query.Features.QueryExecution;

[GenerateSerializer, Alias(PartitionQuerySerializationAliases.OwnedLeafRequest)]
internal sealed record PartitionQueryOwnedLeafRequest(
    [property: Id(0)] PartitionQueryLeafPlan Plan,
    [property: Id(1)] PhysicalShardRecord Owner,
    [property: Id(2)] string Tenant,
    [property: Id(3)] ResourceDefinition Definition,
    [property: Id(4)] ResourceLifetimeCoverage Coverage,
    [property: Id(5)] PrincipalRecord Principal);
