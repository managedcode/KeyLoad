namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnMaintenanceCapabilityAliases.Result)]
internal sealed record AnnMaintenanceCapabilityResult(
    [property: global::Orleans.Id(0)] Guid SessionId,
    [property: global::Orleans.Id(1)] AnnSourceCut? Source,
    [property: global::Orleans.Id(2)] long ThroughSequence,
    [property: global::Orleans.Id(3)] int Count,
    [property: global::Orleans.Id(4)] CommitProjectionBatchRequest? OriginalCheckpointIntent,
    [property: global::Orleans.Id(5)] string? IndexSha256,
    [property: global::Orleans.Id(6)] long CheckpointAfter,
    [property: global::Orleans.Id(7)] bool Pending,
    [property: global::Orleans.Id(8)] AnnMaintenanceResult? OriginalResult = null)
{
    [global::Orleans.Id(9)]
    public CommitProjectionBatchRequest? EnrollmentSettlementIntent
    {
        get; init;
    }
}
