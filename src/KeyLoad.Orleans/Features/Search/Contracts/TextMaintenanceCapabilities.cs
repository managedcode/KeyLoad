namespace KeyLoad.Orleans;

internal enum TextMaintenanceCapabilityKind
{
    Begin,
    PreparePage,
    ApplyIntent,
    SettleCheckpoint,
    Verify,
    Release
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(TextMaintenanceCapabilityAliases.Request)]
internal sealed record TextMaintenanceCapabilityRequest(
    [property: global::Orleans.Id(0)] Guid SessionId,
    [property: global::Orleans.Id(1)] TextIndexMaintenanceRequest Maintenance,
    [property: global::Orleans.Id(2)] TextMaintenanceCapabilityKind Kind,
    [property: global::Orleans.Id(3)] ProjectionBatch? Page = null,
    [property: global::Orleans.Id(4)] CommitProjectionBatchRequest? CheckpointIntent = null,
    [property: global::Orleans.Id(5)] ProjectionBatchResult? Acknowledged = null);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(TextMaintenanceCapabilityAliases.Result)]
internal sealed record TextMaintenanceCapabilityResult(
    [property: global::Orleans.Id(0)] Guid SessionId,
    [property: global::Orleans.Id(1)] TextIndexSourceCut? Source,
    [property: global::Orleans.Id(2)] long Checkpoint,
    [property: global::Orleans.Id(3)] long ThroughSequence,
    [property: global::Orleans.Id(4)] int TrackedRecords,
    [property: global::Orleans.Id(5)] CommitProjectionBatchRequest? OriginalCheckpointIntent,
    [property: global::Orleans.Id(6)] string? IndexSha256,
    [property: global::Orleans.Id(7)] long ReplayUpperSequence);
