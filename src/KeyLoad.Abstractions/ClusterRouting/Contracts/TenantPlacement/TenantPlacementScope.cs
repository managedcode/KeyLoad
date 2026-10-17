namespace KeyLoad;

/// <summary>Defines the bounded TenantPlacementScope contract.</summary>
/// <param name="TenantId">The original TenantId contract value.</param>
/// <param name="DatabaseId">The original DatabaseId contract value.</param>
/// <param name="ControlPartition">The original ControlPartition contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(TenantPlacementPublicProtocolAliases.TenantPlacementScopeAlias)]
public sealed record TenantPlacementScope(
    [property: global::Orleans.Id(0)] string TenantId,
    [property: global::Orleans.Id(1)] string DatabaseId,
    [property: global::Orleans.Id(2)] PartitionRef ControlPartition);
