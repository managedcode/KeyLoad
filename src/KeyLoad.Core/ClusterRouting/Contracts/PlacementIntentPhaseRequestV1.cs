namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PlacementIntentPhaseRequestV1Aliases.PlacementIntentPhaseRequestV1Alias)]
internal sealed record PlacementIntentPhaseRequestV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string TenantId,
    [property: global::Orleans.Id(2)] string DatabaseId,
    [property: global::Orleans.Id(3)] Guid IntentId,
    [property: global::Orleans.Id(4)] long ExpectedRevision,
    [property: global::Orleans.Id(5)] Guid OriginalApplyCommandId,
    [property: global::Orleans.Id(6)] PlacementIntentStatus TargetStatus,
    [property: global::Orleans.Id(7)] PlacementIntentClaimV1 Claim,
    [property: global::Orleans.Id(8)] PartitionMoveResult? OriginalMoveReceipt,
    [property: global::Orleans.Id(9)] bool PreviousClaimOwnerAbsent,
    [property: global::Orleans.Id(10)] PartitionRef ControlPartition,
    [property: global::Orleans.Id(11)] ApplyPlacementIntentRequest OriginalApplyRequest);

internal static class PlacementIntentPhaseRequestV1Aliases
{
    internal const string PlacementIntentPhaseRequestV1Alias = "keyload.cluster-routing.placement-intent-phase.v1";
}
