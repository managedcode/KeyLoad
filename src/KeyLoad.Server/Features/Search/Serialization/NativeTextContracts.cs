using System.Security.Cryptography;
using KeyLoad.Query.Features.Search;

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

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextProtocol.OwnerAlias)]
internal sealed record NativeTextOwnerReceipt(
[property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] string RootDirectory,
    [property: global::Orleans.Id(2)] string GenerationLeaf,
    [property: global::Orleans.Id(3)] Guid SourceNodeId,
    [property: global::Orleans.Id(4)] TextProjectionScope? Scope,
    [property: global::Orleans.Id(5)] NativeTextOwnedPath[] OwnedPaths);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextProtocol.OwnedPathAlias)]
internal sealed record NativeTextOwnedPath(
    [property: global::Orleans.Id(0)] string RelativePath,
    [property: global::Orleans.Id(1)] bool IsDirectory);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextProtocol.RecordAlias)]
internal sealed record NativeTextRecord(
    [property: global::Orleans.Id(0)] ulong Id,
    [property: global::Orleans.Id(1)] EntityRef Reference,
    [property: global::Orleans.Id(2)] long Revision);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextProtocol.ManifestAlias)]
internal sealed record NativeTextManifest(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] TextProjectionScope Scope,
    [property: global::Orleans.Id(2)] string TokenizerVersion,
    [property: global::Orleans.Id(3)] string HashVersion,
    [property: global::Orleans.Id(4)] NativeTextRecord[] Records,
    [property: global::Orleans.Id(5)] NativeTextFile[] Files);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextProtocol.FileAlias)]
internal sealed record NativeTextFile(
    [property: global::Orleans.Id(0)] string RelativePath,
    [property: global::Orleans.Id(1)] long Length,
    [property: global::Orleans.Id(2)] byte[] Sha256);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextProtocol.EnvelopeAlias)]
internal sealed record NativeTextEnvelope(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] byte[] Payload,
    [property: global::Orleans.Id(2)] byte[] Sha256);

internal static class NativeTextEnvelopeCodec
{
    internal static byte[] Encode<T>(T value)
    {
        var payload = NativeSerialization.Serialize(value);
        return NativeSerialization.Serialize(new NativeTextEnvelope(NativeTextProtocol.FormatVersion,
            payload, SHA256.HashData(payload)));
    }

    internal static T Decode<T>(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var envelope = NativeSerialization.Deserialize<NativeTextEnvelope>(bytes);
            if (envelope is null || envelope.FormatVersion != NativeTextProtocol.FormatVersion || envelope.Payload is null
                || envelope.Sha256 is null || envelope.Sha256.Length != SHA256.HashSizeInBytes
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(envelope.Payload), envelope.Sha256))
            {
                throw KeyLoad.Errors.Fail(KeyLoad.ErrorCode.Corruption, NativeTextProtocol.ProjectionCorrupt);
            }
            return NativeSerialization.Deserialize<T>(envelope.Payload);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw NativeTextErrors.Corrupt();
        }
    }
}
