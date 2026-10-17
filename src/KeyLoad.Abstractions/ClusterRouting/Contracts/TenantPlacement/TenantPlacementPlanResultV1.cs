namespace KeyLoad;

/// <summary>Defines the bounded TenantPlacementPlanResultV1 contract.</summary>
/// <param name="Plan">The original Plan contract value.</param>
/// <param name="SignedPlan">The original SignedPlan contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(TenantPlacementPlanV1Aliases.TenantPlacementPlanResultV1Alias)]
public sealed record TenantPlacementPlanResultV1(
    [property: global::Orleans.Id(0)] TenantPlacementPlanV1 Plan,
    [property: global::Orleans.Id(1)] string SignedPlan);
