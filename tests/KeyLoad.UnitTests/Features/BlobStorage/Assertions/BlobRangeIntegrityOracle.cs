using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobRangeIntegrityOracle
{
    private const int HashBytes = 32;
    private const int OrdinalOffset = HashBytes;
    private const int LengthOffset = OrdinalOffset + sizeof(int);
    private const int PartHashOffset = LengthOffset + sizeof(int);
    private const int TranscriptBytes = PartHashOffset + HashBytes;

    internal static string Initial(Guid incarnation, BlobRef blob, Guid upload)
        => Hash(KeyCodec.Encode(BlobRangeIntegrityFixture.ChainSpace,
            incarnation.ToString(BlobRangeIntegrityFixture.GuidFormat), blob.Partition.TenantId,
            blob.Partition.DatabaseId, blob.Partition.TransactionDomainId, blob.Partition.PartitionKey,
            blob.Resource, blob.Id, upload.ToString(BlobRangeIntegrityFixture.GuidFormat),
            (long)BlobRangeIntegrityFixture.PayloadBytes, (long)BlobRangeIntegrityFixture.RawPartBytes));

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static string Next(string preceding, int ordinal, ReadOnlySpan<byte> part)
    {
        Span<byte> transcript = stackalloc byte[TranscriptBytes];
        Convert.FromHexString(preceding).CopyTo(transcript);
        BinaryPrimitives.WriteInt32BigEndian(transcript.Slice(OrdinalOffset, sizeof(int)), ordinal);
        BinaryPrimitives.WriteInt32BigEndian(transcript.Slice(LengthOffset, sizeof(int)), part.Length);
        _ = SHA256.HashData(part, transcript[PartHashOffset..]);
        return Hash(transcript);
    }

    internal static byte[] Key(string prefix, BlobRangeIntegrityFixture fixture, int? ordinal = null)
    {
        var blob = fixture.Blob;
        if (ordinal is { } part)
        {
            return KeyCodec.Encode(prefix, blob.Partition.TenantId, blob.Partition.DatabaseId,
                blob.Partition.TransactionDomainId, blob.Partition.PartitionKey, blob.Resource, blob.Id,
                fixture.UploadId.ToString(BlobRangeIntegrityFixture.GuidFormat), (long)part);
        }
        if (prefix == BlobRangeIntegrityFixture.StateSpace)
        {
            return KeyCodec.Encode(prefix, blob.Partition.TenantId, blob.Partition.DatabaseId,
                blob.Partition.TransactionDomainId, blob.Partition.PartitionKey, blob.Resource, blob.Id,
                fixture.UploadId.ToString(BlobRangeIntegrityFixture.GuidFormat));
        }
        return KeyCodec.Encode(prefix, blob.Partition.TenantId, blob.Partition.DatabaseId,
            blob.Partition.TransactionDomainId, blob.Partition.PartitionKey, blob.Resource, blob.Id);
    }
}
