using System.Buffers.Binary;
using System.Security.Cryptography;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobScopedBytesR42Corpus
{
    internal const int PartBytes = 65536;
    internal const int Length = 65553;
    internal static readonly string[][] PartHashes = [
     ["3053c32ff0f0d2d7ec552ccdfc62be78d771b0b472bbe76bb2e71e67a400c6bb","7f83e874461b394257a4366c9b974fe076d5bb1f11580710f288498ab5fd4d1d"],
  ["51642f1a8d1f5baa91df4452d849009cf9007ef87e7ccc2b916fd7f15536c612","fae5eb7adb848a9d836b74ba536fee3384097c4098dfc2a456781818bfb70207"],
  ["bbc4090938058eb25e0fef69d4cb60694071a3527171028e6c7fde973f88a70d","d99236976868df8cf5ee1df8724531929b39a0bf30a3a20573ed1f73e254dc0a"],
  ["8d8c533ae9504f6a091e25c9093426e89364850c2ba46c9d0290b7b995eae5cd","0d74f71744cebfec039589a9fc8bde43f4cd179d793f6e70d8d73c067456835e"]
    ];
    internal static byte[] Bytes(int scope, long offset, int count)
     => Enumerable.Range(0, count).Select(i => checked((byte)(((offset + i) * 17 + scope * 31 + 7) % 251))).ToArray();
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static string Next(string seed, int ordinal, int count, string hash)
    {
        var bytes = new byte[72];
        Convert.FromHexString(seed).AsSpan().CopyTo(bytes);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(32, 4), ordinal);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(36, 4), count);
        Convert.FromHexString(hash).AsSpan().CopyTo(bytes.AsSpan(40));
        return Hash(bytes);
    }
}
