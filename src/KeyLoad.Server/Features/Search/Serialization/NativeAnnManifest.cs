using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnProtocol
{
    internal const int Version = 1;
    internal const string RootDirectory = "ann-indexes";
    internal const string RootReceipt = "owner.bin";
    internal const string RootLock = ".owner.lock";
    internal const string RootPending = "owner.pending.bin";
    internal const string IndexFile = "index.bin";
    internal const string ManifestFile = "manifest.bin";
    internal const string GenerationPrefix = "generation-";
    internal const string PointerPrefix = "current-";
    internal const string BinaryExtension = ".bin";
    internal const string GuidFormat = "N";
    internal const string RootAlias = "keyload.server.ann-root.v1";
    internal const string ManifestAlias = "keyload.server.ann-manifest.v1";
    internal const string PointerAlias = "keyload.server.ann-pointer.v1";
    internal const string OwnedKeyAlias = "keyload.server.ann-owned-key.v1";
    internal const string PendingAlias = "keyload.server.ann-pending-replay.v1";
    internal const string ReplayPageAlias = "keyload.server.ann-replay-page.v1";
    internal const string PublishedAlias = "keyload.server.ann-published.v1";
    internal const string Corrupt = "The native ANN generation is corrupt.";
    internal const string Ownership = "The native ANN generation is not owned by this node.";
    internal const string Stale = "The native ANN generation is no longer current.";
    internal const string InvalidSource = "The native ANN generation does not match its admitted canonical source.";
    internal const string Bound = "The native ANN generation reservation is exceeded.";
    internal const string MissingDependencyHistory = "The native ANN dependency history is unavailable; explicitly rebuild the generation.";
    internal const string Interrupted = AnnMaintenanceProtocol.Interrupted;
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeAnnProtocol.RootAlias)]
internal sealed record NativeAnnRootReceipt(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string Root,
    [property: global::Orleans.Id(2)] Guid NodeId,
    [property: global::Orleans.Id(3)] Guid Incarnation,
    [property: global::Orleans.Id(4)] string[] OwnedGenerations,
    [property: global::Orleans.Id(5)] NativeAnnPublished[] Published,
    [property: global::Orleans.Id(6)] NativeAnnPublished[] Pending);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeAnnProtocol.OwnedKeyAlias)]
internal sealed record NativeAnnOwnedKey(
    [property: global::Orleans.Id(0)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(1)] long IndexGeneration);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeAnnProtocol.ManifestAlias)]
internal sealed record NativeAnnManifest(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string GenerationLeaf,
    [property: global::Orleans.Id(2)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(3)] long IndexGeneration,
    [property: global::Orleans.Id(4)] string PrincipalId,
    [property: global::Orleans.Id(5)] string Collection,
    [property: global::Orleans.Id(6)] string Field,
    [property: global::Orleans.Id(7)] VectorSpace Space,
    [property: global::Orleans.Id(8)] PhysicalShardRecord Placement,
    [property: global::Orleans.Id(9)] AnnSourceCut Source,
    [property: global::Orleans.Id(10)] PackedAnnOptions Policy,
    [property: global::Orleans.Id(11)] int Count,
    [property: global::Orleans.Id(12)] byte[] IndexSha256,
    [property: global::Orleans.Id(13)] CommitProjectionBatchRequest? CheckpointIntent)
{
    [global::Orleans.Id(14)] public bool IsPending { get; init; }
    [global::Orleans.Id(15)] public long ReplayAfter { get; init; }
    [global::Orleans.Id(16)] public long ReplayThrough { get; init; }
    [global::Orleans.Id(17)] public string? ReplayCorpusSha256 { get; init; }
    [global::Orleans.Id(18)] public bool ExplicitBuildStage { get; init; }
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeAnnProtocol.PointerAlias)]
internal sealed record NativeAnnPointer(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string GenerationLeaf,
    [property: global::Orleans.Id(2)] byte[] ManifestSha256);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeAnnProtocol.PublishedAlias)]
internal sealed record NativeAnnPublished(
    [property: global::Orleans.Id(0)] NativeAnnOwnedKey Key,
    [property: global::Orleans.Id(1)] NativeAnnPointer Pointer);
