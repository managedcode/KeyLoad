using System.Buffers.Binary;
using System.Security.Cryptography;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPrincipalRevokedR44Corpus
{
    internal const int Length = 65553;
    internal static readonly string[] PartHashes = ["8329281ceb8929a422362aada31c88d27ecd99f5d801c85f755c8ff5862180ea", "b1d6d8f2ba0d1fb8e4350700b8d3588e35cecd62efd48568926c8799cd85ab6d"];
    internal static byte[] Bytes(long offset, int count)
        => Enumerable.Range(0, count).Select(i => checked((byte)(((offset + i) * 13 + 5) % 241))).ToArray();
    internal static string Next(string seed, int ordinal, int count, string hash)
    {
        Span<byte> input = stackalloc byte[72];
        Convert.FromHexString(seed).AsSpan().CopyTo(input);
        BinaryPrimitives.WriteInt32BigEndian(input.Slice(32, 4), ordinal);
        BinaryPrimitives.WriteInt32BigEndian(input.Slice(36, 4), count);
        Convert.FromHexString(hash).AsSpan().CopyTo(input[40..]);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }
}
