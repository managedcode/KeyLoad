using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace KeyLoad.Storage;

public sealed class MissingValue
{
    private MissingValue() { }
    public static MissingValue Instance { get; } = new();
}

/// <summary>Version 1 binary keys; strings use UTF-8 byte order and zero escaping.</summary>
public static class KeyCodec
{
    public const byte Version = 1;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static byte[] Encode(params object?[] components)
    {
        using var stream = new MemoryStream();
        stream.WriteByte(Version);
        foreach (var component in components) Write(stream, component);
        return stream.ToArray();
    }

    public static object?[] Decode(ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty || key[0] != Version) throw Errors.Fail(ErrorCode.FormatUnsupported, "Unknown key codec version.");
        var values = new List<object?>();
        var offset = 1;
        while (offset < key.Length)
        {
            var tag = key[offset++];
            object? value = tag switch
            {
                0x10 => MissingValue.Instance,
                0x11 => null,
                0x20 => ReadByte(key, ref offset) switch { 0 => false, 1 => true, _ => throw BadKey() },
                0x30 => unchecked((long)(ReadUInt64(key, ref offset) ^ (1UL << 63))),
                0x31 => ReadDecimal(key, ref offset),
                0x32 => ReadDouble(key, ref offset),
                0x40 => new DateTimeOffset(unchecked((long)(ReadUInt64(key, ref offset) ^ (1UL << 63))), TimeSpan.Zero),
                0x50 => Utf8.GetString(ReadEscaped(key, ref offset)),
                0x60 => ReadEscaped(key, ref offset),
                _ => throw BadKey()
            };
            values.Add(value);
            if (values.Count > 256) throw BadKey();
        }
        return values.ToArray();
    }

    private static void Write(Stream stream, object? value)
    {
        Span<byte> buffer = stackalloc byte[8];
        switch (value)
        {
            case MissingValue: stream.WriteByte(0x10); break;
            case null: stream.WriteByte(0x11); break;
            case bool boolean: stream.WriteByte(0x20); stream.WriteByte(boolean ? (byte)1 : (byte)0); break;
            case int integer: Write(stream, (long)integer); break;
            case long integer:
                stream.WriteByte(0x30);
                BinaryPrimitives.WriteUInt64BigEndian(buffer, unchecked((ulong)integer) ^ (1UL << 63));
                stream.Write(buffer);
                break;
            case decimal number: WriteDecimal(stream, number); break;
            case double number:
                if (!double.IsFinite(number)) throw Errors.Fail(ErrorCode.Validation, "Indexed numbers must be finite.");
                stream.WriteByte(0x32);
                var bits = BitConverter.DoubleToUInt64Bits(number == 0 ? 0 : number);
                BinaryPrimitives.WriteUInt64BigEndian(buffer, (bits & (1UL << 63)) != 0 ? ~bits : bits ^ (1UL << 63));
                stream.Write(buffer);
                break;
            case DateTimeOffset time:
                stream.WriteByte(0x40);
                BinaryPrimitives.WriteUInt64BigEndian(buffer, unchecked((ulong)time.UtcTicks) ^ (1UL << 63));
                stream.Write(buffer);
                break;
            case string text: stream.WriteByte(0x50); WriteEscaped(stream, Utf8.GetBytes(text)); break;
            case byte[] bytes: stream.WriteByte(0x60); WriteEscaped(stream, bytes); break;
            case Guid id: Write(stream, id.ToString("N")); break;
            default: throw Errors.Fail(ErrorCode.UnsupportedCapability, "This type is not supported by key codec v1.");
        }
    }

    private static void WriteEscaped(Stream stream, ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            stream.WriteByte(b);
            if (b == 0) stream.WriteByte(0xFF);
        }
        stream.WriteByte(0);
        stream.WriteByte(0);
    }

    private static byte[] ReadEscaped(ReadOnlySpan<byte> key, ref int offset)
    {
        using var bytes = new MemoryStream();
        while (offset < key.Length)
        {
            var b = ReadByte(key, ref offset);
            if (b != 0) { bytes.WriteByte(b); continue; }
            var escape = ReadByte(key, ref offset);
            if (escape == 0) return bytes.ToArray();
            if (escape != 0xFF) throw BadKey();
            bytes.WriteByte(0);
        }
        throw BadKey();
    }

    private static void WriteDecimal(Stream stream, decimal value)
    {
        stream.WriteByte(0x31);
        if (value == 0) { stream.WriteByte(1); return; }
        var negative = value < 0;
        stream.WriteByte(negative ? (byte)0 : (byte)2);
        var parts = decimal.GetBits(decimal.Abs(value));
        var magnitude = (BigInteger)(uint)parts[0] | (BigInteger)(uint)parts[1] << 32 | (BigInteger)(uint)parts[2] << 64;
        var scale = (parts[3] >> 16) & 0xFF;
        var digits = magnitude.ToString(CultureInfo.InvariantCulture);
        var exponent = digits.Length - scale;
        digits = digits.TrimEnd('0');
        Put((byte)(exponent + 64));
        foreach (var digit in digits) Put((byte)(digit - '0' + 1));
        Put(0);
        void Put(byte b) => stream.WriteByte(negative ? (byte)~b : b);
    }

    private static decimal ReadDecimal(ReadOnlySpan<byte> key, ref int offset)
    {
        var sign = ReadByte(key, ref offset);
        if (sign == 1) return 0;
        if (sign is not (0 or 2)) throw BadKey();
        var exponent = ReadByte(key, ref offset);
        if (sign == 0) exponent = (byte)~exponent;
        var digits = new StringBuilder(29);
        while (true)
        {
            var digit = ReadByte(key, ref offset);
            if (sign == 0) digit = (byte)~digit;
            if (digit == 0) break;
            if (digit > 10 || digits.Length == 29) throw BadKey();
            digits.Append((char)('0' + digit - 1));
        }
        if (digits.Length == 0) throw BadKey();
        return decimal.Parse($"{(sign == 0 ? "-" : "")}{digits}e{exponent - 64 - digits.Length}", NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static double ReadDouble(ReadOnlySpan<byte> key, ref int offset)
    {
        var sortable = ReadUInt64(key, ref offset);
        var bits = (sortable & (1UL << 63)) == 0 ? ~sortable : sortable ^ (1UL << 63);
        var number = BitConverter.UInt64BitsToDouble(bits);
        if (!double.IsFinite(number)) throw BadKey();
        return number;
    }

    private static byte ReadByte(ReadOnlySpan<byte> key, ref int offset)
    {
        if ((uint)offset >= (uint)key.Length) throw BadKey();
        return key[offset++];
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> key, ref int offset)
    {
        if (key.Length - offset < 8) throw BadKey();
        var value = BinaryPrimitives.ReadUInt64BigEndian(key[offset..]);
        offset += 8;
        return value;
    }

    private static KeyLoadException BadKey() => Errors.Fail(ErrorCode.Corruption, "Invalid encoded key.");
}

public sealed class BinaryKeyComparer : IComparer<byte[]>
{
    public static BinaryKeyComparer Instance { get; } = new();
    public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y.AsSpan());
}
