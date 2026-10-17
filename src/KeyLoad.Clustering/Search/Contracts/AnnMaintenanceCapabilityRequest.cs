namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnMaintenanceCapabilityAliases.Request)]
internal sealed record AnnMaintenanceCapabilityRequest(
    [property: global::Orleans.Id(0)] AnnMaintenanceRequest Maintenance,
    [property: global::Orleans.Id(1)] Guid SessionId,
    [property: global::Orleans.Id(2)] AnnMaintenanceCapabilityKind Kind,
    [property: global::Orleans.Id(3)] ProjectionBatch? Page,
    [property: global::Orleans.Id(4)] CommitProjectionBatchRequest? CheckpointIntent,
    [property: global::Orleans.Id(5)] AnnMaintenanceResult? FullOriginalResult = null,
    [property: global::Orleans.Id(6)] ProjectionConsumerInfo? OriginalConfigure = null);
