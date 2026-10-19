using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Core.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(Bm25CorpusProtocol.CurrentAlias)]
internal sealed record Bm25CorpusCurrent(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] PartitionRef Partition,
    [property: global::Orleans.Id(2)] string Collection,
    [property: global::Orleans.Id(3)] string Field,
    [property: global::Orleans.Id(4)] string TokenizerVersion,
    [property: global::Orleans.Id(5)] string PrincipalId,
    [property: global::Orleans.Id(6)] long PolicyEpoch,
    [property: global::Orleans.Id(7)] long SchemaVersion,
    [property: global::Orleans.Id(8)] string ResourcePolicyDigest,
    [property: global::Orleans.Id(9)] PhysicalShardRecord SourceOwner,
    [property: global::Orleans.Id(10)] long SourceOutboxTail,
    [property: global::Orleans.Id(11)] long CapturePosition,
    [property: global::Orleans.Id(12)] long CaptureApplied,
    [property: global::Orleans.Id(13)] long CaptureReadGeneration,
    [property: global::Orleans.Id(14)] Guid Incarnation,
    [property: global::Orleans.Id(15)] DistributedTextStatistics Statistics,
    [property: global::Orleans.Id(16)] Guid Generation,
    [property: global::Orleans.Id(17)] string PayloadDigest);
