using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeTextIncrementalAliases.Manifest)]
internal sealed record NativeTextIncrementalManifest(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] TextProjectionScope Scope,
    [property: global::Orleans.Id(2)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(3)] long Generation,
    [property: global::Orleans.Id(4)] PhysicalShardRecord Placement,
    [property: global::Orleans.Id(5)] long ThroughSequence,
    [property: global::Orleans.Id(6)] long AppliedPosition,
    [property: global::Orleans.Id(7)] ulong NextRecord,
    [property: global::Orleans.Id(8)] NativeTextIncrementalRecord[] Records,
    [property: global::Orleans.Id(9)] NativeTextFile[] Files,
    [property: global::Orleans.Id(10)] string TokenizerVersion,
    [property: global::Orleans.Id(11)] string HashVersion,
    [property: global::Orleans.Id(12)] string ResourceSha256,
    [property: global::Orleans.Id(13)] bool Bootstrap);
