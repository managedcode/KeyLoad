using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.CrashHost.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobCompleteCommitCutOracle
{
    private const string ChainSpace = "blob-chain-v1";
    private const string GuidFormat = "N";
    internal const int ChunkBytes = 65_536;
    internal const int MultipartBytes = 65_553;
    internal const int TailBytes = 17;
    internal const int UploadLifetimeSeconds = 3_600;

    internal static byte[] OriginalBytes() => [1, 2, 3, 4];
    internal static byte[] MultipartPayload() => Enumerable.Range(0, MultipartBytes).Select(static i => (byte)(i % 251)).ToArray();

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static string Chain(Guid incarnation, BlobRef blob, Guid upload, byte[] bytes)
    {
        var hash = Initial(incarnation, blob, upload, bytes.Length);
        for (var offset = 0; offset < bytes.Length; offset += ChunkBytes)
        {
            hash = Next(hash, offset / ChunkBytes, bytes.AsSpan(offset, Math.Min(ChunkBytes, bytes.Length - offset)));
        }
        return hash;
    }

    internal static string Initial(Guid incarnation, BlobRef blob, Guid upload, int length)
        => Hash(KeyCodec.Encode(ChainSpace, incarnation.ToString(GuidFormat), blob.Partition.TenantId,
            blob.Partition.DatabaseId, blob.Partition.TransactionDomainId, blob.Partition.PartitionKey,
            blob.Resource, blob.Id, upload.ToString(GuidFormat), (long)length, (long)ChunkBytes));

    internal static string Next(string prior, int ordinal, ReadOnlySpan<byte> bytes)
    {
        Span<byte> transcript = stackalloc byte[72];
        Convert.FromHexString(prior).CopyTo(transcript);
        BinaryPrimitives.WriteInt32BigEndian(transcript.Slice(32, 4), ordinal);
        BinaryPrimitives.WriteInt32BigEndian(transcript.Slice(36, 4), bytes.Length);
        _ = SHA256.HashData(bytes, transcript[40..]);
        return Hash(transcript);
    }

    internal static ReplicatedOperation Begin(BlobCommitCutSeedProof proof, Guid upload)
        => proof.Operations.Select(static bytes => NativeSerialization.Deserialize<ReplicatedOperation>(bytes))
            .Single(operation => operation.Kind == OperationKind.BeginBlobUpload
                && NativeCommandPayload.Read<BeginBlobUploadRequest>(operation).UploadId == upload);

    internal static ReplicatedOperation PriorComplete(BlobCommitCutSeedProof proof)
        => proof.Operations.Select(static bytes => NativeSerialization.Deserialize<ReplicatedOperation>(bytes))
            .Single(operation => operation.Kind == OperationKind.CompleteBlobUpload);

    internal static BlobMetadata Metadata(BlobCommitCutSeedProof proof, ReplicatedOperation complete, Guid upload,
        byte[] bytes, long revision)
        => new(BlobCommitCutProtocol.Blob, revision, upload, bytes.Length,
            bytes.Length == MultipartBytes ? 2 : 1,
            Chain(proof.Incarnation, BlobCommitCutProtocol.Blob, upload, bytes), new RowAccess(), complete.EvaluatedAt);

    internal static async Task RequireSelectedAsync(BlobCommitCutSeedProof proof, ReplicatedOperation operation)
    {
        await Assert.That(operation.Id).IsEqualTo(BlobCommitCutProtocol.CompleteId);
        await Assert.That(operation.Kind).IsEqualTo(OperationKind.CompleteBlobUpload);
        await Assert.That(operation.PrincipalId).IsEqualTo(BlobCommitCutProtocol.Principal);
        await ExactAsync(NativeCommandPayload.Read<CompleteBlobUploadRequest>(operation),
            new CompleteBlobUploadRequest(BlobCommitCutProtocol.CompleteId, BlobCommitCutProtocol.Blob,
                BlobCommitCutProtocol.NewUpload, Chain(proof.Incarnation, BlobCommitCutProtocol.Blob,
                    BlobCommitCutProtocol.NewUpload, MultipartPayload())));
    }

    internal static async Task ExactAsync<T>(T actual, T expected)
        => await Assert.That(NativeSerialization.Serialize(actual).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
}
