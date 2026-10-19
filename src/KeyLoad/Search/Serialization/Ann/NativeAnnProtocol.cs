namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnProtocol
{
    internal const int Version = 2;
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
