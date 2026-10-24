using System.Collections.Immutable;

namespace KeyLoad.Server.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProtocol.PendingAlias)]
internal sealed record AnnPendingReplay(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] AnnManifest AdmittedUpper,
    [property: global::Orleans.Id(2)] long AfterSequence,
    [property: global::Orleans.Id(3)] long ThroughSequence,
    [property: global::Orleans.Id(4)] ImmutableArray<VectorRecord> Records,
    [property: global::Orleans.Id(5)] string CorpusSha256,
    [property: global::Orleans.Id(6)] CommitProjectionBatchRequest CheckpointIntent);
