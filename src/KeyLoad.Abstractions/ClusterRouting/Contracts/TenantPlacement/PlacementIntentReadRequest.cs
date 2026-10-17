namespace KeyLoad;

/// <summary>Defines the bounded PlacementIntentReadRequest contract.</summary>
/// <param name="Scope">The original Scope contract value.</param>
/// <param name="IntentId">The original IntentId contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(TenantPlacementPublicProtocolAliases.PlacementIntentReadRequestAlias)]
public sealed record PlacementIntentReadRequest(
    [property: global::Orleans.Id(0)] TenantPlacementScope Scope,
    [property: global::Orleans.Id(1)] Guid IntentId);
