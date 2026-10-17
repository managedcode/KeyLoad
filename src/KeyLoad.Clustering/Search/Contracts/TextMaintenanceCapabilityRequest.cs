namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(TextMaintenanceCapabilityAliases.Request)]
internal sealed record TextMaintenanceCapabilityRequest(
    [property: global::Orleans.Id(0)] Guid SessionId,
    [property: global::Orleans.Id(1)] TextIndexMaintenanceRequest Maintenance,
    [property: global::Orleans.Id(2)] TextMaintenanceCapabilityKind Kind,
    [property: global::Orleans.Id(3)] ProjectionBatch? Page = null,
    [property: global::Orleans.Id(4)] CommitProjectionBatchRequest? CheckpointIntent = null,
    [property: global::Orleans.Id(5)] ProjectionBatchResult? Acknowledged = null);
