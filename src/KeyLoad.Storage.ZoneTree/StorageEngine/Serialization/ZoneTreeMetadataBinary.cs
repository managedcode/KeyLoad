using System.Buffers.Binary;
using KeyLoad.InternalSerialization;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeMetadataBinary
{
    internal const ulong IdentityMagic = 0x364449444C4BUL;
    private const int PrefixBytes = sizeof(ulong);

    internal static byte[] Write<T>(T value, ulong magic)
    {
        var payload = OrleansSerialization.Serialize(value);
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

        // Metadata files are read back from disk and from backups, so their contract is checked where they enter.
        var value = OrleansSerialization.Deserialize<T>(bytes[PrefixBytes..]);
        ContractValidation.Validate(value);
        return value;
    }
}
