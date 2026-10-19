namespace KeyLoad.Server.Features.Search;

internal static class TextProtocol
{
    internal const string OwnerAlias = "keyload.server.execution-text.owner";
    internal const string ManifestAlias = "keyload.server.execution-text.manifest";
    internal const string RecordAlias = "keyload.server.execution-text.record";
    internal const string EnvelopeAlias = "keyload.server.execution-text.envelope";
    internal const string FileAlias = "keyload.server.execution-text.file";
    internal const string OwnedPathAlias = "keyload.server.execution-text.owned-path";
    internal const string RootReceiptFile = ".execution-text.root.bin";
    internal const string OwnerFile = "owner.bin";
    internal const string ManifestFile = "manifest.bin";
    internal const string PendingManifestFile = "manifest.pending";
    internal const string OwnerPendingFile = "owner.pending";
    internal const string RuntimeDirectoryCore = "execution";
    internal const string GenerationPrefix = "generation-";
    internal const int FormatVersion = 1;
    internal const int KeyBytes = sizeof(ulong) * 3;
    internal const int ValueBytes = sizeof(byte);
    internal const int PostingBytes = KeyBytes + ValueBytes;
    internal const string InvalidProjection = "The execution text projection is invalid.";
    internal const string ProjectionBusy = "The execution text projection is busy.";
    internal const string ProjectionBoundExceeded = "The execution text projection exceeds its configured bound.";
    internal const string ProjectionCorrupt = "The execution text projection is corrupt.";
    internal const string ProjectionOwnership = "The execution text projection directory is not owned by this node.";
    internal const string ProjectionMismatch = "The execution text projection does not match the authorized source cut.";
}
