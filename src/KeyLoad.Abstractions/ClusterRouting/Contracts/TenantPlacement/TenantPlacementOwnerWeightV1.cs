namespace KeyLoad;

/// <summary>Defines the bounded TenantPlacementOwnerWeightV1 contract.</summary>
/// <param name="PhysicalShardId">The original PhysicalShardId contract value.</param>
/// <param name="Weight">The original Weight contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(TenantPlacementPolicyV1Aliases.TenantPlacementOwnerWeightV1Alias)]
public sealed record TenantPlacementOwnerWeightV1(
    [property: global::Orleans.Id(0)] Guid PhysicalShardId,
    [property: global::Orleans.Id(1)] int Weight);
