using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobGlobalQuotaR49Corpus
{
    internal const string FirstResource = "global-first";
    internal const string SecondResource = "global-second";
    internal const string ControlResource = "global-control";
    internal const string HeldName = "held-reservation";
    internal const string TargetName = "healthy-target";
    internal const string ControlName = "published-control";
    private const string ChainDomain = "blob-chain-v1";
    private const string GuidFormat = "N";
    internal const long FirstReservation = 600_000_000L;
    internal const long SecondReservation = 500_000_000L;
    internal const int PartLength = 65_536;
    internal const int SmallLength = 65_540;
    internal static byte[] Bytes(int offset, int count)
    {
        var bytes = new byte[count];
        for (var index = 0; index < count; index++)
        {
            bytes[index] = (byte)(((offset + index) * 17L + 3L) % 251L);
        }
        return bytes;
    }
    internal static string Initial(Guid creationIncarnation, BeginBlobUploadRequest request)
    {
        var partition = request.Blob.Partition;
        var preimage = KeyCodec.Encode(ChainDomain, creationIncarnation.ToString(GuidFormat),
            partition.TenantId, partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey,
            request.Blob.Resource, request.Blob.Id, request.UploadId.ToString(GuidFormat), request.Length, (long)PartLength);
        return Convert.ToHexStringLower(SHA256.HashData(preimage));
    }
    internal static string PartHash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static string Next(string before, int ordinal, ReadOnlySpan<byte> bytes)
    {
        Span<byte> preimage = stackalloc byte[72];
        Convert.FromHexString(before).AsSpan().CopyTo(preimage);
        BinaryPrimitives.WriteInt32BigEndian(preimage[32..36], ordinal);
        BinaryPrimitives.WriteInt32BigEndian(preimage[36..40], bytes.Length);
        SHA256.HashData(bytes).AsSpan().CopyTo(preimage[40..]);
        return Convert.ToHexStringLower(SHA256.HashData(preimage));
    }
}
