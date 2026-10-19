using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeTextIncrementalAliases.Intent)]
internal sealed record NativeTextIncrementalIntent(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] Guid MaintenanceCommandId,
    [property: global::Orleans.Id(2)] TextProjectionScope Scope,
    [property: global::Orleans.Id(3)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(4)] long Generation,
    [property: global::Orleans.Id(5)] PhysicalShardRecord Placement,
    [property: global::Orleans.Id(6)] long PreviousSequence,
    [property: global::Orleans.Id(7)] long ThroughSequence,
    [property: global::Orleans.Id(8)] CommitProjectionBatchRequest CheckpointCommand,
    [property: global::Orleans.Id(9)] NativeTextIncrementalChange[] Changes,
    [property: global::Orleans.Id(10)] NativeTextIncrementalRecord[] Records,
    [property: global::Orleans.Id(11)] ulong NextRecord,
    [property: global::Orleans.Id(12)] bool Bootstrap,
    [property: global::Orleans.Id(13)] string ResourceSha256,
    [property: global::Orleans.Id(14)] long SourceUpperSequence,
    [property: global::Orleans.Id(15)] long AppliedPosition);
