using System.Buffers.Binary;

namespace KeyLoad.Storage;

internal static class KeyCodecReadPrimitives
{
    private const int UInt64Bytes = sizeof(ulong);
    private const int SortableSignBit = 63;
    private const string InvalidEncodedKey = "Invalid encoded key.";

    internal static double ReadDouble(ReadOnlySpan<byte> key, ref int offset)
    {
        var sortable = ReadUInt64(key, ref offset);
        var bits = (sortable & (1UL << SortableSignBit)) == 0 ? ~sortable : sortable ^ (1UL << SortableSignBit);
        var number = BitConverter.UInt64BitsToDouble(bits);
        if (!double.IsFinite(number))
        {
            throw BadKey();
        }

        return number;
    }

    internal static byte ReadByte(ReadOnlySpan<byte> key, ref int offset)
    {
        if ((uint)offset >= (uint)key.Length)
        {
            throw BadKey();
        }

        return key[offset++];
    }

    internal static ulong ReadUInt64(ReadOnlySpan<byte> key, ref int offset)
    {
        if (key.Length - offset < UInt64Bytes)
        {
            throw BadKey();
        }

        var value = BinaryPrimitives.ReadUInt64BigEndian(key[offset..]);
        offset += UInt64Bytes;
        return value;
    }

    internal static KeyLoadException BadKey() => Errors.Fail(ErrorCode.Corruption, InvalidEncodedKey);
}
