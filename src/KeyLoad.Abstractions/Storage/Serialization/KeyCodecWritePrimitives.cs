using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using static KeyLoad.Storage.KeyCodecTokens;

namespace KeyLoad.Storage;

/// <summary>Writes one typed key component in the version 1 sortable encoding.</summary>
internal static class KeyCodecWritePrimitives
{
    private const int StackTextBytes = 256;
    private const string InvalidTextMessage = "Key text is not valid Unicode.";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static void Write(ref KeyBufferWriter writer, in KeyComponent component)
    {
        switch (component.Kind)
        {
            case KeyComponentKind.Missing:
                writer.Write((byte)MissingTag);
                break;
            case KeyComponentKind.Null:
                writer.Write((byte)NullTag);
                break;
            case KeyComponentKind.Boolean:
                writer.Write((byte)BooleanTag);
                writer.Write(component.Boolean ? (byte)TruePayload : (byte)FalsePayload);
                break;
            case KeyComponentKind.WholeNumber:
                WriteInt64(ref writer, component.WholeNumber);
                break;
            case KeyComponentKind.FixedPoint:
                KeyCodecDecimal.Write(ref writer, component.ExactNumber);
                break;
            case KeyComponentKind.FloatingPoint:
                WriteDouble(ref writer, component.RealNumber);
                break;
            case KeyComponentKind.Timestamp:
                WriteOrdered(ref writer, (byte)TimestampTag, unchecked((ulong)component.UtcTicks) ^ SortableSignMask);
                break;
            case KeyComponentKind.Text:
                WriteText(ref writer, component.Text);
                break;
            default:
                writer.Write((byte)BinaryTag);
                writer.WriteEscaped(component.Binary);
                break;
        }
    }

    private static void WriteInt64(ref KeyBufferWriter writer, long value)
        => WriteOrdered(ref writer, (byte)Int64Tag, unchecked((ulong)value) ^ SortableSignMask);

    private static void WriteDouble(ref KeyBufferWriter writer, double value)
    {
        if (!double.IsFinite(value))
        {
            throw Errors.Fail(ErrorCode.Validation, NonfiniteNumber);
        }

        var bits = BitConverter.DoubleToUInt64Bits(value == ZeroNumber ? ZeroNumber : value);
        WriteOrdered(ref writer, (byte)DoubleTag, (bits & SortableSignMask) != NoSetBits ? ~bits : bits ^ SortableSignMask);
    }

    private static void WriteOrdered(ref KeyBufferWriter writer, byte tag, ulong ordered)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(buffer, ordered);
        writer.Write(tag);
        writer.Write(buffer);
    }

    private static void WriteText(ref KeyBufferWriter writer, string value)
    {
        writer.Write((byte)TextTag);
        int count;
        try
        {
            count = Utf8.GetByteCount(value);
        }
        catch (EncoderFallbackException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidTextMessage);
        }

        if (count <= StackTextBytes)
        {
            Span<byte> encoded = stackalloc byte[StackTextBytes];
            writer.WriteEscaped(encoded[..Utf8.GetBytes(value, encoded)]);
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(count);
        try
        {
            writer.WriteEscaped(rented.AsSpan(0, Utf8.GetBytes(value, rented)));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
