using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobLaterPartOracle
{
    internal static byte[] Payload() => Enumerable.Range(0, 65_553).Select(static i => (byte)(i % 239)).ToArray();

    internal static string Initial(Guid incarnation, BlobRef blob, Guid upload)
        => BlobPriorPublicationOracle.Hash(KeyCodec.Encode("blob-chain-v1", incarnation.ToString("N"),
            blob.Partition.TenantId, blob.Partition.DatabaseId, blob.Partition.TransactionDomainId,
            blob.Partition.PartitionKey, blob.Resource, blob.Id, upload.ToString("N"), 65_553L, 65_536L));

    internal static string Next(string previous, int ordinal, ReadOnlySpan<byte> bytes)
    {
        Span<byte> transcript = stackalloc byte[72];
        Convert.FromHexString(previous).CopyTo(transcript);
        BinaryPrimitives.WriteInt32BigEndian(transcript.Slice(32, 4), ordinal);
        BinaryPrimitives.WriteInt32BigEndian(transcript.Slice(36, 4), bytes.Length);
        _ = SHA256.HashData(bytes, transcript[40..]);
        return BlobPriorPublicationOracle.Hash(transcript);
    }

    internal static byte[] Key(string family, BlobPriorPublicationState seed, int ordinal)
    {
        var blob = seed.Blob;
        return KeyCodec.Encode(family, blob.Partition.TenantId, blob.Partition.DatabaseId,
            blob.Partition.TransactionDomainId, blob.Partition.PartitionKey, blob.Resource, blob.Id,
            seed.Upload.ToString("N"), (long)ordinal);
    }
}
