using System.Buffers.Binary;
using System.Text;
using static KeyLoad.Storage.KeyCodecTokens;

namespace KeyLoad.Storage;

internal static class KeyCodecReadPrimitives
{
    private const int UInt64Bytes = sizeof(ulong);
    private const string InvalidEncodedKey = "Invalid encoded key.";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static double ReadDouble(ReadOnlySpan<byte> key, ref int offset)
    {
        var sortable = ReadUInt64(key, ref offset);
        var bits = (sortable & SortableSignMask) == NoSetBits ? ~sortable : sortable ^ SortableSignMask;
        if (bits == SortableSignMask)
        {
            throw BadKey();
        }

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

    internal static DateTimeOffset ReadTimestamp(ReadOnlySpan<byte> key, ref int offset)
    {
        var ticks = unchecked((long)(ReadUInt64(key, ref offset) ^ SortableSignMask));
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            throw BadKey();
        }

        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    internal static string ReadText(ReadOnlySpan<byte> key, ref int offset)
    {
        var bytes = ReadEscaped(key, ref offset);
        try
        {
            return Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw BadKey();
        }
    }

    internal static byte[] ReadEscaped(ReadOnlySpan<byte> key, ref int offset)
    {
        using var bytes = new MemoryStream();
        while (offset < key.Length)
        {
            var value = ReadByte(key, ref offset);
            if (value != EscapedZeroByte)
            {
                bytes.WriteByte(value);
                continue;
            }

            var escape = ReadByte(key, ref offset);
            if (escape == EscapedZeroByte)
            {
                return bytes.ToArray();
            }

            if (escape != ZeroEscapeMarker)
            {
                throw BadKey();
            }

            bytes.WriteByte(EscapedZeroByte);
        }

        throw BadKey();
    }

    internal static KeyLoadException BadKey() => Errors.Fail(ErrorCode.Corruption, InvalidEncodedKey);
}
