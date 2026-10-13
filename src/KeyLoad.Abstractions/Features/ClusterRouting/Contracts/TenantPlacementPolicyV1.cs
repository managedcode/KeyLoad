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

/// <summary>Defines the bounded TenantPlacementOwnerWeightV1 contract.</summary>
/// <param name="PhysicalShardId">The original PhysicalShardId contract value.</param>
/// <param name="Weight">The original Weight contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(TenantPlacementPolicyV1Aliases.TenantPlacementOwnerWeightV1Alias)]
public sealed record TenantPlacementOwnerWeightV1(
    [property: global::Orleans.Id(0)] Guid PhysicalShardId,
    [property: global::Orleans.Id(1)] int Weight);

/// <summary>Defines the bounded ConfigurePlacementPolicyRequest contract.</summary>
/// <param name="ExpectedRevision">The original ExpectedRevision contract value.</param>
/// <param name="Policy">The original Policy contract value.</param>
/// <param name="ControlPartition">The original ControlPartition contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(TenantPlacementPolicyV1Aliases.ConfigurePlacementPolicyRequestAlias)]
public sealed record ConfigurePlacementPolicyRequest(
    [property: global::Orleans.Id(0)] long ExpectedRevision,
    [property: global::Orleans.Id(1)] TenantPlacementPolicyV1 Policy,
    [property: global::Orleans.Id(2)] PartitionRef ControlPartition);

internal static class TenantPlacementPolicyV1Aliases
{
    internal const string TenantPlacementPolicyV1Alias = "keyload.cluster-routing.tenant-placement-policy.v1";
    internal const string TenantPlacementOwnerWeightV1Alias = "keyload.cluster-routing.tenant-placement-owner-weight.v1";
    internal const string ConfigurePlacementPolicyRequestAlias = "keyload.cluster-routing.configure-placement-policy.v1";
}
