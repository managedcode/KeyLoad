using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Defines the bounded PlacementIntentStatus contract.</summary>
public enum PlacementIntentStatus
{
    /// <summary>Identifies the absence of an admitted intent phase.</summary>
    None = 0,
    /// <summary>Identifies the Pending durable phase.</summary>
    Pending = 1,
    /// <summary>Identifies the Applying durable phase.</summary>
    Applying = 2,
    /// <summary>Identifies the Paused durable phase.</summary>
    Paused = 3,
    /// <summary>Identifies the Completed durable phase.</summary>
    Completed = 4
}

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

internal static class PlacementIntentV1Aliases
{
    internal const string PlacementIntentV1Alias = "keyload.cluster-routing.placement-intent.v1";
    internal const string PersistPlacementIntentRequestAlias = "keyload.cluster-routing.persist-placement-intent.v1";
    internal const string ApplyPlacementIntentRequestAlias = "keyload.cluster-routing.apply-placement-intent.v1";
}
