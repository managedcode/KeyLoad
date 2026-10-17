using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobPriorPublicationOracle
{
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static string Initial(Guid incarnation, BlobRef blob, Guid upload) => Hash(KeyCodec.Encode("blob-chain-v1",
        incarnation.ToString("N"), blob.Partition.TenantId, blob.Partition.DatabaseId,
        blob.Partition.TransactionDomainId, blob.Partition.PartitionKey, blob.Resource, blob.Id,
        upload.ToString("N"), 4L, 65_536L));

    internal static string Complete(string initial, ReadOnlySpan<byte> bytes)
    {
        Span<byte> transcript = stackalloc byte[72];
        Convert.FromHexString(initial).CopyTo(transcript);
        BinaryPrimitives.WriteInt32BigEndian(transcript.Slice(32, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(transcript.Slice(36, 4), 4);
        _ = SHA256.HashData(bytes, transcript[40..]);
        return Hash(transcript);
    }

    internal static byte[] Key(string prefix, BlobPriorPublicationState seed)
    {
        var blob = seed.Blob;
        var fields = new object[] { prefix, blob.Partition.TenantId, blob.Partition.DatabaseId,
            blob.Partition.TransactionDomainId, blob.Partition.PartitionKey, blob.Resource, blob.Id };
        if (prefix == "blob-state-v1")
        {
            return KeyCodec.Encode([.. fields, seed.Upload.ToString("N")]);
        }
        if (prefix is "blob-part-v1" or "blob-partmeta-v1")
        {
            return KeyCodec.Encode([.. fields, seed.Upload.ToString("N"), 0L]);
        }
        return KeyCodec.Encode(fields);
    }
}
