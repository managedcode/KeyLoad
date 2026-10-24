using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Server.Search;

internal static class TextHash
{
    internal static ulong Sha256(string token)
    {
        var utf8 = Encoding.UTF8.GetBytes(token);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(utf8, digest);
        return BinaryPrimitives.ReadUInt64LittleEndian(digest);
    }
}
