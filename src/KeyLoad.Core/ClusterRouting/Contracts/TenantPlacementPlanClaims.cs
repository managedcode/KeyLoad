using System.Collections.Immutable;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(TenantPlacementPlanClaimsAliases.TenantPlacementPlanClaimsAlias)]
internal sealed record TenantPlacementPlanClaims(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string Purpose,
    [property: global::Orleans.Id(2)] Guid Incarnation,
    [property: global::Orleans.Id(3)] string PrincipalId,
    [property: global::Orleans.Id(4)] long PolicyEpoch,
    [property: global::Orleans.Id(5)] PhysicalShardRecord ControlOwner,
    [property: global::Orleans.Id(6)] TenantPlacementPlanV1 Plan,
    [property: global::Orleans.Id(7)] ImmutableArray<PlacementCapacityWitnessV1> OriginalCapacityWitnesses);

internal static class TenantPlacementPlanClaimsAliases
{
    internal const string TenantPlacementPlanClaimsAlias = "keyload.cluster-routing.tenant-placement-plan-claims.v1";
}
