namespace KeyLoad;

/// <summary>Defines the bounded PersistPlacementIntentRequest contract.</summary>
/// <param name="IntentId">The original IntentId contract value.</param>
/// <param name="ExpectedRevision">The original ExpectedRevision contract value.</param>
/// <param name="SignedPlan">The original SignedPlan contract value.</param>
/// <param name="ControlPartition">The original ControlPartition contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(PlacementIntentV1Aliases.PersistPlacementIntentRequestAlias)]
public sealed record PersistPlacementIntentRequest(
    [property: global::Orleans.Id(0)] Guid IntentId,
    [property: global::Orleans.Id(1)] long ExpectedRevision,
    [property: global::Orleans.Id(2)] string SignedPlan,
    [property: global::Orleans.Id(3)] PartitionRef ControlPartition);
