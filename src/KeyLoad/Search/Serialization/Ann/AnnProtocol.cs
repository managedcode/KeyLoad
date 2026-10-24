namespace KeyLoad.Server.Search;

internal static class AnnProtocol
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
    internal const string RootAlias = "keyload.server.ann-root";
    internal const string ManifestAlias = "keyload.server.ann-manifest";
    internal const string PointerAlias = "keyload.server.ann-pointer";
    internal const string OwnedKeyAlias = "keyload.server.ann-owned-key";
    internal const string PendingAlias = "keyload.server.ann-pending-replay";
    internal const string ReplayPageAlias = "keyload.server.ann-replay-page";
    internal const string PublishedAlias = "keyload.server.ann-published";
    internal const string Corrupt = "The execution ANN generation is corrupt.";
    internal const string Ownership = "The execution ANN generation is not owned by this node.";
    internal const string Stale = "The execution ANN generation is no longer current.";
    internal const string InvalidSource = "The execution ANN generation does not match its admitted canonical source.";
    internal const string Bound = "The execution ANN generation reservation is exceeded.";
    internal const string MissingDependencyHistory = "The execution ANN dependency history is unavailable; explicitly rebuild the generation.";
    internal const string Interrupted = AnnMaintenanceProtocol.Interrupted;
}
