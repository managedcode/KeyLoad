using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Query.Features.QueryExecution;

[GenerateSerializer, Alias(PartitionQuerySerializationAliases.OwnedLeafRequestV2)]
internal sealed record PartitionQueryOwnedLeafRequestV2(
    [property: Id(0)] PartitionQueryOwnedLeafRequest Original,
    [property: Id(1)] ResourceDefinition Definition,
    [property: Id(2)] ResourceLifetimeCoverageV1 Coverage,
    [property: Id(3)] PrincipalRecord Principal);
