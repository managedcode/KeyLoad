using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Query.Features.QueryExecution;

[GenerateSerializer, Alias(DistributedSearchAliases.OwnedLeafV2)]
internal sealed record DistributedSearchOwnedLeafV2(
    [property: Id(0)] DistributedSearchOwnedLeafV1 Original,
    [property: Id(1)] ResourceDefinition Definition,
    [property: Id(2)] ResourceLifetimeCoverageV1 Coverage,
    [property: Id(3)] PrincipalRecord Principal);
