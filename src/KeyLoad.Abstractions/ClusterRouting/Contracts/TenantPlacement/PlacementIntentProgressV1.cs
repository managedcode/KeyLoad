namespace KeyLoad;

/// <summary>Defines the bounded PlacementIntentProgressV1 contract.</summary>
/// <param name="IntentId">The original IntentId contract value.</param>
/// <param name="Revision">The original Revision contract value.</param>
/// <param name="CompletedMoves">The original CompletedMoves contract value.</param>
/// <param name="TotalMoves">The original TotalMoves contract value.</param>
/// <param name="OriginalMoveId">The original OriginalMoveId contract value.</param>
/// <param name="ActualPhase">The original ActualPhase contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(TenantPlacementPublicProtocolAliases.PlacementIntentProgressV1Alias)]
public sealed record PlacementIntentProgressV1(
    [property: global::Orleans.Id(0)] Guid IntentId,
    [property: global::Orleans.Id(1)] long Revision,
    [property: global::Orleans.Id(2)] int CompletedMoves,
    [property: global::Orleans.Id(3)] int TotalMoves,
    [property: global::Orleans.Id(4)] Guid OriginalMoveId,
    [property: global::Orleans.Id(5)] PartitionMovePhase ActualPhase);
