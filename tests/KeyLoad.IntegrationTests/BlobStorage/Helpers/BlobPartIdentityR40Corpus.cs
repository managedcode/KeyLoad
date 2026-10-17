using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPartIdentityR40Corpus
{
    internal const int FirstLength = 65536;
    internal const int Length = 65540;
    internal const string OriginalSha = "473ace3a4940d727fc122fb4fe8282356d26d01d046acdf892e72710e25acb49";
    internal const string ChangedSha = "600674ec4a2fe41a1aa20bde7690dab4ddcea63b3819ab0becb7ff839ac771cd";
    internal const string TailSha = "f4e3f0b04771c047e227c9ecaba65d3fe2fd0e1eee0a7552b956d1a7c535a7cf";

    internal static byte[] Bytes(int offset, int count)
    {
        byte[] tail = [16, 32, 48, 64];
        return Enumerable.Range(offset, count).Select(index => index < FirstLength
            ? checked((byte)((index * 29 + 7) % 251)) : tail[index - FirstLength]).ToArray();
    }

    internal static byte[] Changed()
    {
        var bytes = Bytes(0, FirstLength);
        bytes[17] ^= 1;
        return bytes;
    }

    internal static string Next(string previous, int ordinal, int count, string partSha)
    {
        var bytes = new byte[72];
        Convert.FromHexString(previous).CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(32, 4), ordinal);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(36, 4), count);
        Convert.FromHexString(partSha).CopyTo(bytes, 40);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
