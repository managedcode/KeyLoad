namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(TextMaintenanceCapabilityAliases.Result)]
internal sealed record TextMaintenanceCapabilityResult(
    [property: global::Orleans.Id(0)] Guid SessionId,
    [property: global::Orleans.Id(1)] TextIndexSourceCut? Source,
    [property: global::Orleans.Id(2)] long Checkpoint,
    [property: global::Orleans.Id(3)] long ThroughSequence,
    [property: global::Orleans.Id(4)] int TrackedRecords,
    [property: global::Orleans.Id(5)] CommitProjectionBatchRequest? OriginalCheckpointIntent,
    [property: global::Orleans.Id(6)] string? IndexSha256,
    [property: global::Orleans.Id(7)] long ReplayUpperSequence,
    [property: global::Orleans.Id(8)] CommitProjectionBatchRequest? LastSettledCheckpointRequest = null);
