namespace KeyLoad;

/// <summary>Defines the bounded ApplyPlacementIntentRequest contract.</summary>
/// <param name="TenantId">The original TenantId contract value.</param>
/// <param name="DatabaseId">The original DatabaseId contract value.</param>
/// <param name="IntentId">The original IntentId contract value.</param>
/// <param name="ExpectedRevision">The original ExpectedRevision contract value.</param>
/// <param name="ControlPartition">The original ControlPartition contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(PlacementIntentV1Aliases.ApplyPlacementIntentRequestAlias)]
public sealed record ApplyPlacementIntentRequest(
    [property: global::Orleans.Id(0)] string TenantId,
    [property: global::Orleans.Id(1)] string DatabaseId,
    [property: global::Orleans.Id(2)] Guid IntentId,
    [property: global::Orleans.Id(3)] long ExpectedRevision,
    [property: global::Orleans.Id(4)] PartitionRef ControlPartition);
