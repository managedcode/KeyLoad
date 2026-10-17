using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Defines the bounded PlacementIntentV1 contract.</summary>
/// <param name="Version">The original Version contract value.</param>
/// <param name="IntentId">The original IntentId contract value.</param>
/// <param name="PrincipalId">The original PrincipalId contract value.</param>
/// <param name="PolicyEpoch">The original PolicyEpoch contract value.</param>
/// <param name="Plan">The original Plan contract value.</param>
/// <param name="Revision">The original Revision contract value.</param>
/// <param name="CompletedMoves">The original CompletedMoves contract value.</param>
/// <param name="Status">The original Status contract value.</param>
/// <param name="Receipts">The original Receipts contract value.</param>
/// <param name="OriginalApplyCommandId">The original OriginalApplyCommandId contract value.</param>
/// <param name="Claim">The original Claim contract value.</param>
/// <param name="ControlOwner">The original ControlOwner contract value.</param>
/// <param name="OriginalApplyRequest">The original OriginalApplyRequest contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(PlacementIntentV1Aliases.PlacementIntentV1Alias)]
public sealed record PlacementIntentV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid IntentId,
    [property: global::Orleans.Id(2)] string PrincipalId,
    [property: global::Orleans.Id(3)] long PolicyEpoch,
    [property: global::Orleans.Id(4)] TenantPlacementPlanV1 Plan,
    [property: global::Orleans.Id(5)] long Revision,
    [property: global::Orleans.Id(6)] int CompletedMoves,
    [property: global::Orleans.Id(7)] PlacementIntentStatus Status,
    [property: global::Orleans.Id(8)] ImmutableArray<PartitionMoveResult> Receipts,
    [property: global::Orleans.Id(9)] Guid? OriginalApplyCommandId,
    [property: global::Orleans.Id(10)] PlacementIntentClaimV1? Claim,
    [property: global::Orleans.Id(11)] PhysicalShardRecord ControlOwner,
    [property: global::Orleans.Id(12)] ApplyPlacementIntentRequest? OriginalApplyRequest);
