using System.Buffers.Binary;
using System.Text;
using static KeyLoad.Storage.KeyCodecReadPrimitives;

namespace KeyLoad.Storage;

/// <summary>Represents an indexed key component which is absent rather than explicitly null.</summary>
public sealed class MissingValue
{
    private MissingValue() { }

    /// <summary>Gets the singleton missing-value marker.</summary>
    public static MissingValue Instance { get; } = new();
}

/// <summary>Version 1 binary keys; strings use UTF-8 byte order and zero escaping.</summary>
public static class KeyCodec
{
    /// <summary>Gets the version byte written at the start of every encoded key.</summary>
    public const byte Version = 1;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>Encodes the supplied components into a version 1 sortable binary key.</summary>
    /// <param name="components">The key components, in their comparison order.</param>
    /// <returns>The encoded key bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="components"/> is null.</exception>
    /// <exception cref="KeyLoadException">A component is unsupported or cannot be encoded.</exception>
    public static byte[] Encode(params object?[] components)
    {
        ArgumentNullException.ThrowIfNull(components);

        using var stream = new MemoryStream();
        stream.WriteByte(Version);
        foreach (var component in components)
        {
            Write(stream, component);
        }

        return stream.ToArray();
    }

    /// <summary>Decodes a version 1 sortable binary key into its components.</summary>
    /// <param name="key">The encoded key bytes.</param>
    /// <returns>The decoded components in their original order.</returns>
    /// <exception cref="KeyLoadException">The key is unsupported or malformed.</exception>
    public static object?[] Decode(ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty || key[0] != Version)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, "Unknown key codec version.");
        }

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
                0x31 => KeyCodecDecimal.Read(key, ref offset),
                0x32 => ReadDouble(key, ref offset),
                0x40 => new DateTimeOffset(unchecked((long)(ReadUInt64(key, ref offset) ^ (1UL << 63))), TimeSpan.Zero),
                0x50 => Utf8.GetString(ReadEscaped(key, ref offset)),
                0x60 => ReadEscaped(key, ref offset),
                _ => throw BadKey()
            };
            values.Add(value);
            if (values.Count > 256)
            {
                throw BadKey();
            }
        }
        return values.ToArray();
    }

    private static void Write(Stream stream, object? value)
    {
        switch (value)
        {
            case MissingValue:
                WriteMarker(stream, 0x10);
                break;
            case null:
                WriteMarker(stream, 0x11);
                break;
            case bool boolean:
                WriteBoolean(stream, boolean);
                break;
            case int integer:
                Write(stream, (long)integer);
                break;
            case long integer:
                WriteInt64(stream, integer);
                break;
            case decimal number:
                KeyCodecDecimal.Write(stream, number);
                break;
            case double number:
                WriteDouble(stream, number);
                break;
            case DateTimeOffset time:
                WriteDateTimeOffset(stream, time);
                break;
            case string text:
                WriteText(stream, text);
                break;
            case byte[] bytes:
                WriteBinary(stream, bytes);
                break;
            case Guid id:
                Write(stream, id.ToString("N"));
                break;
            default:
                throw Errors.Fail(ErrorCode.UnsupportedCapability, "This type is not supported by key codec v1.");
        }
    }

    private static void WriteMarker(Stream stream, byte marker) => stream.WriteByte(marker);

    private static void WriteBoolean(Stream stream, bool value)
    {
        stream.WriteByte(0x20);
        stream.WriteByte(value ? (byte)1 : (byte)0);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        stream.WriteByte(0x30);
        BinaryPrimitives.WriteUInt64BigEndian(buffer, unchecked((ulong)value) ^ (1UL << 63));
        stream.Write(buffer);
    }

    private static void WriteDouble(Stream stream, double value)
    {
        if (!double.IsFinite(value))
        {
            throw Errors.Fail(ErrorCode.Validation, "Indexed numbers must be finite.");
        }

        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        stream.WriteByte(0x32);
        var bits = BitConverter.DoubleToUInt64Bits(value == 0 ? 0 : value);
        BinaryPrimitives.WriteUInt64BigEndian(buffer, (bits & (1UL << 63)) != 0 ? ~bits : bits ^ (1UL << 63));
        stream.Write(buffer);
    }

    private static void WriteDateTimeOffset(Stream stream, DateTimeOffset value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        stream.WriteByte(0x40);
        BinaryPrimitives.WriteUInt64BigEndian(buffer, unchecked((ulong)value.UtcTicks) ^ (1UL << 63));
        stream.Write(buffer);
    }

    private static void WriteText(Stream stream, string value)
    {
        stream.WriteByte(0x50);
        WriteEscaped(stream, Utf8.GetBytes(value));
    }

    private static void WriteBinary(Stream stream, byte[] value)
    {
        stream.WriteByte(0x60);
        WriteEscaped(stream, value);
    }

    private static void WriteEscaped(Stream stream, ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            stream.WriteByte(b);
            if (b == 0)
            {
                stream.WriteByte(0xFF);
            }
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
            if (b != 0)
            {
                bytes.WriteByte(b);
                continue;
            }

            var escape = ReadByte(key, ref offset);
            if (escape == 0)
            {
                return bytes.ToArray();
            }

            if (escape != 0xFF)
            {
                throw BadKey();
            }

            bytes.WriteByte(0);
        }
        throw BadKey();
    }

}

/// <summary>Compares encoded keys using unsigned lexicographic byte ordering.</summary>
public sealed class BinaryKeyComparer : IComparer<byte[]>
{
    /// <summary>Gets the shared binary key comparer.</summary>
    public static BinaryKeyComparer Instance { get; } = new();

    /// <summary>Compares two byte arrays lexicographically; null is treated as an empty array.</summary>
    /// <param name="x">The first key to compare.</param>
    /// <param name="y">The second key to compare.</param>
    /// <returns>A negative value when <paramref name="x"/> precedes <paramref name="y"/>, zero when they are equal, or a positive value otherwise.</returns>
    public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y.AsSpan());
}
