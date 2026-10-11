using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementOutcomeBlobOracle
{
    private const string Domain = "blob-chain-v1";
    private const string GuidFormat = "N";
    private const int Length = 4;
    private const int RawPartBytes = 65_536;
    private const int DigestBytes = 32;
    private const int Ordinal = 0;
    private const int LengthOffset = DigestBytes + sizeof(int);
    private const int DigestOffset = LengthOffset + sizeof(int);
    private const int TranscriptBytes = DigestOffset + DigestBytes;

    internal static string Initial(Guid incarnation)
    {
        var blob = ControlledPartitionMovementBlobSeed.Blob;
        var partition = blob.Partition;
        return Convert.ToHexStringLower(SHA256.HashData(KeyCodec.Encode(Domain, incarnation.ToString(GuidFormat),
            partition.TenantId, partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey,
            blob.Resource, blob.Id, ControlledPartitionMovementBlobSeed.UploadId.ToString(GuidFormat),
            (long)Length, (long)RawPartBytes)));
    }

    internal static string Complete(Guid incarnation)
    {
        Span<byte> transcript = stackalloc byte[TranscriptBytes];
        Convert.FromHexString(Initial(incarnation)).CopyTo(transcript);
        BinaryPrimitives.WriteInt32BigEndian(transcript.Slice(DigestBytes, sizeof(int)), Ordinal);
        BinaryPrimitives.WriteInt32BigEndian(transcript.Slice(LengthOffset, sizeof(int)), Length);
        Convert.FromHexString(ControlledPartitionMovementBlobSeed.PartSha256).CopyTo(transcript[DigestOffset..]);
        return Convert.ToHexStringLower(SHA256.HashData(transcript));
    }
}
