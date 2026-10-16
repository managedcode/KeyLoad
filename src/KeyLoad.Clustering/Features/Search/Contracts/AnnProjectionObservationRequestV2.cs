using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProjectionLifetimeObservationScope.RequestAlias)]
internal sealed record AnnProjectionObservationRequestV2(
    [property: global::Orleans.Id(0)] AnnProjectionObservationRequest Original,
    [property: global::Orleans.Id(1)] ResourceDefinition Definition,
    [property: global::Orleans.Id(2)] ResourceLifetimeCoverageV1 Coverage,
    [property: global::Orleans.Id(3)] PrincipalRecord Principal,
    [property: global::Orleans.Id(4)] ImmutableArray<ResourceDefinition> Definitions);
