namespace KeyLoad.Orleans.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(OnlineTextRequestScope.CapabilityResultAlias)]
internal sealed record OnlineTextCapabilityResult(
    [property: global::Orleans.Id(0)] Guid SessionId,
    [property: global::Orleans.Id(1)] TextIndexSourceCut? BaseCut,
    [property: global::Orleans.Id(2)] TextIndexSourceCut? CurrentCut,
    [property: global::Orleans.Id(3)] long ThroughSequence,
    [property: global::Orleans.Id(4)] int TrackedRecords,
    [property: global::Orleans.Id(5)] string? IndexSha256,
    [property: global::Orleans.Id(6)] OnlineTextIndexMaintenanceResult? OriginalResult,
    [property: global::Orleans.Id(7)] ReplicatedOperation? IssuedPublication,
    [property: global::Orleans.Id(8)] CommitProjectionBatchRequest? CheckpointIntent,
    [property: global::Orleans.Id(9)] Guid? CurrentCommandId);
