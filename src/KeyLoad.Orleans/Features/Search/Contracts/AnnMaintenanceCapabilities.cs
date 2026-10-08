namespace KeyLoad.Orleans;

internal enum AnnMaintenanceCapabilityKind
{
    Begin,
    Load,
    ApplyPage,
    StagePage,
    Verify,
    Publish,
    Abort,
    Release
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnMaintenanceCapabilityAliases.Request)]
internal sealed record AnnMaintenanceCapabilityRequest(
    [property: global::Orleans.Id(0)] AnnMaintenanceRequest Maintenance,
    [property: global::Orleans.Id(1)] Guid SessionId,
    [property: global::Orleans.Id(2)] AnnMaintenanceCapabilityKind Kind,
    [property: global::Orleans.Id(3)] ProjectionBatch? Page,
    [property: global::Orleans.Id(4)] CommitProjectionBatchRequest? CheckpointIntent);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnMaintenanceCapabilityAliases.Result)]
internal sealed record AnnMaintenanceCapabilityResult(
    [property: global::Orleans.Id(0)] Guid SessionId,
    [property: global::Orleans.Id(1)] AnnSourceCut? Source,
    [property: global::Orleans.Id(2)] long ThroughSequence,
    [property: global::Orleans.Id(3)] int Count,
    [property: global::Orleans.Id(4)] CommitProjectionBatchRequest? OriginalCheckpointIntent,
    [property: global::Orleans.Id(5)] string? IndexSha256,
    [property: global::Orleans.Id(6)] long CheckpointAfter,
    [property: global::Orleans.Id(7)] bool Pending);

internal static class AnnMaintenanceCapabilityAliases
{
    internal const string Request = "keyload.orleans.ann-maintenance-capability.v1";
    internal const string Result = "keyload.orleans.ann-maintenance-capability-result.v1";
}
