namespace KeyLoad.Orleans.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(OnlineTextRequestScope.CapabilityRequestAlias)]
internal sealed record OnlineTextCapabilityRequest(
    [property: global::Orleans.Id(0)] Guid SessionId,
    [property: global::Orleans.Id(1)] OnlineTextIndexMaintenanceRequest Request,
    [property: global::Orleans.Id(2)] OnlineTextCapabilityKind Kind,
    [property: global::Orleans.Id(3)] ProjectionBatch? Page,
    [property: global::Orleans.Id(4)] CommitProjectionBatchRequest? CheckpointIntent,
    [property: global::Orleans.Id(5)] ProjectionBatchResult? Acknowledged);
