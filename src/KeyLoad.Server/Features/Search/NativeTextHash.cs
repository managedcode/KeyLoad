using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextHash
{
    internal static ulong Sha256(string token)
    {
        var utf8 = Encoding.UTF8.GetBytes(token);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(utf8, digest);
        return BinaryPrimitives.ReadUInt64LittleEndian(digest);
    }
}
