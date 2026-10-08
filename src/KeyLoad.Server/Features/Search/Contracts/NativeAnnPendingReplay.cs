using System.Collections.Immutable;

namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeAnnProtocol.PendingAlias)]
internal sealed record NativeAnnPendingReplay(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] NativeAnnManifest AdmittedUpper,
    [property: global::Orleans.Id(2)] long AfterSequence,
    [property: global::Orleans.Id(3)] long ThroughSequence,
    [property: global::Orleans.Id(4)] ImmutableArray<VectorRecord> Records,
    [property: global::Orleans.Id(5)] string CorpusSha256,
    [property: global::Orleans.Id(6)] CommitProjectionBatchRequest CheckpointIntent);
