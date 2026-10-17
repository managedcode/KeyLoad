using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Defines the bounded TenantPlacementPolicyV1 contract.</summary>
/// <param name="Version">The original Version contract value.</param>
/// <param name="TenantId">The original TenantId contract value.</param>
/// <param name="DatabaseId">The original DatabaseId contract value.</param>
/// <param name="Revision">The original Revision contract value.</param>
/// <param name="Enabled">The original Enabled contract value.</param>
/// <param name="PromotionBytes">The original PromotionBytes contract value.</param>
/// <param name="DemotionBytes">The original DemotionBytes contract value.</param>
/// <param name="MinimumFreeBytes">The original MinimumFreeBytes contract value.</param>
/// <param name="MinimumFreeBasisPoints">The original MinimumFreeBasisPoints contract value.</param>
/// <param name="MaximumMemoryBasisPoints">The original MaximumMemoryBasisPoints contract value.</param>
/// <param name="MaximumCpuBasisPoints">The original MaximumCpuBasisPoints contract value.</param>
/// <param name="RequireDistinctHosts">The original RequireDistinctHosts contract value.</param>
/// <param name="OwnerWeights">The original OwnerWeights contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(TenantPlacementPolicyV1Aliases.TenantPlacementPolicyV1Alias)]
public sealed record TenantPlacementPolicyV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string TenantId,
    [property: global::Orleans.Id(2)] string DatabaseId,
    [property: global::Orleans.Id(3)] long Revision,
    [property: global::Orleans.Id(4)] bool Enabled,
    [property: global::Orleans.Id(5)] long PromotionBytes,
    [property: global::Orleans.Id(6)] long DemotionBytes,
    [property: global::Orleans.Id(7)] long MinimumFreeBytes,
    [property: global::Orleans.Id(8)] int MinimumFreeBasisPoints,
    [property: global::Orleans.Id(9)] int MaximumMemoryBasisPoints,
    [property: global::Orleans.Id(10)] int MaximumCpuBasisPoints,
    [property: global::Orleans.Id(11)] bool RequireDistinctHosts,
    [property: global::Orleans.Id(12)] ImmutableArray<TenantPlacementOwnerWeightV1> OwnerWeights);
