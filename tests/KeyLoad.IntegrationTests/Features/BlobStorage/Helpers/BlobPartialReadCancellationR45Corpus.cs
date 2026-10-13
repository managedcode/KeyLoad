using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPartialReadCancellationR45Corpus
{
    internal const int Length = 65553;
    internal const long BoundaryOffset = 65534L;
    internal const int BoundaryCount = 6;
    internal static readonly string[] PartHashes = ["2a0b291a1a224c650a3b8a04bbae02990e6d52bb24a9f6c9e650dea1d6fc7900", "b54f1135ab4f2ee2e07bc834b8b99d67e8ce90c940797a951fe60a168f6e7790"];
    internal static byte[] Bytes(long offset, int count)
        => Enumerable.Range(0, count).Select(i => checked((byte)(((offset + i) * 23 + 11) % 239))).ToArray();
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
