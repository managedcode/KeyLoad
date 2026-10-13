using System.Buffers.Binary;
using System.Security.Cryptography;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobSelectiveReadR43Integrity
{
    private const int DigestBytes = 32;
    private const int PartDigestOffset = 40;
    private const int ChainBytes = 72;
    internal static string Part(int index) => index == 0
        ? "98f608e5aa56c97d8cf793e433cb3e2afd1bc5e6fa051e968a851c352b4406a0"
        : "dc1fcf7238db9eeb4bf0698cc779fa5c88cf4403fa272877904b22ae5ded180d";
    internal static string Next(string originalSeed, int length, string part)
    {
        Span<byte> preimage = stackalloc byte[ChainBytes];
        Convert.FromHexString(originalSeed).AsSpan().CopyTo(preimage);
        BinaryPrimitives.WriteInt32BigEndian(preimage.Slice(DigestBytes, sizeof(int)), 0);
        BinaryPrimitives.WriteInt32BigEndian(preimage.Slice(DigestBytes + sizeof(int), sizeof(int)), length);
        Convert.FromHexString(part).AsSpan().CopyTo(preimage[PartDigestOffset..]);
        return Convert.ToHexStringLower(SHA256.HashData(preimage));
    }
}
