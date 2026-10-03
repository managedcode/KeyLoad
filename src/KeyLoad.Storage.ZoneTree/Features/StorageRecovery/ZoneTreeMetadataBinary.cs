using System.Buffers.Binary;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeMetadataBinary
{
    internal const ulong IdentityMagic = 0x354449444C4BUL;
    internal const ulong BackupMagic = 0x32504B424C4BUL;
    private const int PrefixBytes = sizeof(ulong);

    internal static byte[] Write<T>(T value, ulong magic)
    {
        var payload = NativeSerialization.Serialize(value);
        var bytes = new byte[checked(PrefixBytes + payload.Length)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, magic);
        payload.CopyTo(bytes, PrefixBytes);
        return bytes;
    }

    internal static T Read<T>(ReadOnlySpan<byte> bytes, ulong magic, string unsupported)
    {
        if (bytes.Length <= PrefixBytes || BinaryPrimitives.ReadUInt64LittleEndian(bytes) != magic)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, unsupported);
        }

        return NativeSerialization.Deserialize<T>(bytes[PrefixBytes..]);
    }
}
