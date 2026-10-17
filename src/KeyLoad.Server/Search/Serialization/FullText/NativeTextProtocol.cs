namespace KeyLoad.Server.Features.Search;

internal static class NativeTextProtocol
{
    internal const string OwnerAlias = "keyload.server.native-text.owner.v1";
    internal const string ManifestAlias = "keyload.server.native-text.manifest.v1";
    internal const string RecordAlias = "keyload.server.native-text.record.v1";
    internal const string EnvelopeAlias = "keyload.server.native-text.envelope.v1";
    internal const string FileAlias = "keyload.server.native-text.file.v1";
    internal const string OwnedPathAlias = "keyload.server.native-text.owned-path.v1";
    internal const string RootReceiptFile = ".native-text.root.bin";
    internal const string OwnerFile = "owner.bin";
    internal const string ManifestFile = "manifest.bin";
    internal const string PendingManifestFile = "manifest.pending";
    internal const string OwnerPendingFile = "owner.pending";
    internal const string NativeDirectory = "native";
    internal const string GenerationPrefix = "generation-";
    internal const int FormatVersion = 1;
    internal const int KeyBytes = sizeof(ulong) * 3;
    internal const int ValueBytes = sizeof(byte);
    internal const int PostingBytes = KeyBytes + ValueBytes;
    internal const string InvalidProjection = "The native text projection is invalid.";
    internal const string ProjectionBusy = "The native text projection is busy.";
    internal const string ProjectionBoundExceeded = "The native text projection exceeds its configured bound.";
    internal const string ProjectionCorrupt = "The native text projection is corrupt.";
    internal const string ProjectionOwnership = "The native text projection directory is not owned by this node.";
    internal const string ProjectionMismatch = "The native text projection does not match the authorized source cut.";
}
