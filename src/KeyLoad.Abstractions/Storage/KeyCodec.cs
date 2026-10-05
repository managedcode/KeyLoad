using System.Buffers.Binary;
using System.Text;
using static KeyLoad.Storage.KeyCodecReadPrimitives;
using static KeyLoad.Storage.KeyCodecTokens;

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
    private const int MaximumComponents = 256;
    private const string TooManyComponentsMessage = "Key component count exceeds the supported limit.";
    private const string InvalidTextMessage = "Key text is not valid Unicode.";
    private const string GuidKeyFormat = "N";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>Encodes the supplied components into a version 1 sortable binary key.</summary>
    /// <param name="components">The key components, in their comparison order.</param>
    /// <returns>The encoded key bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="components"/> is null.</exception>
    /// <exception cref="KeyLoadException">A component is unsupported or cannot be encoded.</exception>
    public static byte[] Encode(params object?[] components)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Length > MaximumComponents)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, TooManyComponentsMessage);
        }

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
        if (key.IsEmpty || key[VersionOffset] != Version)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, UnknownVersion);
        }

        var values = new List<object?>();
        var offset = VersionBytes;
        while (offset < key.Length)
        {
            if (values.Count == MaximumComponents)
            {
                throw BadKey();
            }

            var tag = key[offset++];
            object? value = tag switch
            {
                MissingTag => MissingValue.Instance,
                NullTag => null,
                BooleanTag => ReadByte(key, ref offset) switch { FalsePayload => false, TruePayload => true, _ => throw BadKey() },
                Int64Tag => unchecked((long)(ReadUInt64(key, ref offset) ^ SortableSignMask)),
                DecimalTag => KeyCodecDecimal.Read(key, ref offset),
                DoubleTag => ReadDouble(key, ref offset),
                TimestampTag => ReadTimestamp(key, ref offset),
                TextTag => ReadText(key, ref offset),
                BinaryTag => ReadEscaped(key, ref offset),
                _ => throw BadKey()
            };
            values.Add(value);
        }
        return values.ToArray();
    }

    private static void Write(Stream stream, object? value)
    {
        switch (value)
        {
            case MissingValue:
                WriteMarker(stream, MissingTag);
                break;
            case null:
                WriteMarker(stream, NullTag);
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
                Write(stream, id.ToString(GuidKeyFormat));
                break;
            default:
                throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedComponent);
        }
    }

    private static void WriteMarker(Stream stream, byte marker) => stream.WriteByte(marker);

    private static void WriteBoolean(Stream stream, bool value)
    {
        stream.WriteByte(BooleanTag);
        stream.WriteByte(value ? (byte)TruePayload : (byte)FalsePayload);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        stream.WriteByte(Int64Tag);
        BinaryPrimitives.WriteUInt64BigEndian(buffer, unchecked((ulong)value) ^ SortableSignMask);
        stream.Write(buffer);
    }

    private static void WriteDouble(Stream stream, double value)
    {
        if (!double.IsFinite(value))
        {
            throw Errors.Fail(ErrorCode.Validation, NonfiniteNumber);
        }

        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        stream.WriteByte(DoubleTag);
        var bits = BitConverter.DoubleToUInt64Bits(value == ZeroNumber ? ZeroNumber : value);
        BinaryPrimitives.WriteUInt64BigEndian(buffer, (bits & SortableSignMask) != NoSetBits ? ~bits : bits ^ SortableSignMask);
        stream.Write(buffer);
    }

    private static void WriteDateTimeOffset(Stream stream, DateTimeOffset value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        stream.WriteByte(TimestampTag);
        BinaryPrimitives.WriteUInt64BigEndian(buffer, unchecked((ulong)value.UtcTicks) ^ SortableSignMask);
        stream.Write(buffer);
    }

    private static void WriteText(Stream stream, string value)
    {
        stream.WriteByte(TextTag);
        try
        {
            WriteEscaped(stream, Utf8.GetBytes(value));
        }
        catch (EncoderFallbackException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidTextMessage);
        }
    }

    private static void WriteBinary(Stream stream, byte[] value)
    {
        stream.WriteByte(BinaryTag);
        WriteEscaped(stream, value);
    }

    private static void WriteEscaped(Stream stream, ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            stream.WriteByte(b);
            if (b == EscapedZeroByte)
            {
                stream.WriteByte(ZeroEscapeMarker);
            }
        }
        stream.WriteByte(EscapedZeroByte);
        stream.WriteByte(EscapedZeroByte);
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
