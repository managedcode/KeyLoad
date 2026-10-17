using System.Buffers.Binary;
using System.Text;

namespace KeyLoad.Server;

internal ref struct PostgresPayloadReader(ReadOnlySpan<byte> bytes)
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const string InvalidPayload = "The native SQL payload is invalid.";
    private ReadOnlySpan<byte> remaining = bytes;

    internal int Int32()
    {
        var value = BinaryPrimitives.ReadInt32BigEndian(Take(sizeof(int)));
        return value;
    }

    internal short Int16() => BinaryPrimitives.ReadInt16BigEndian(Take(sizeof(short)));

    internal byte Byte() => Take(sizeof(byte))[PostgresProtocol.FirstField];

    internal ReadOnlySpan<byte> Take(int count)
    {
        if (count < PostgresProtocol.EmptyBytes || count > remaining.Length)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPayload);
        }
        var result = remaining[..count];
        remaining = remaining[count..];
        return result;
    }

    internal string Text(int maximumBytes)
    {
        var end = remaining.IndexOf(PostgresProtocol.TextTerminator);
        if (end < PostgresProtocol.FirstField || end > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPayload);
        }
        string value;
        try
        {
            value = Utf8.GetString(Take(end));
        }
        catch (DecoderFallbackException) { throw Errors.Fail(ErrorCode.Validation, InvalidPayload); }
        _ = Take(sizeof(byte));
        return value;
    }

    internal readonly void Complete()
    {
        if (!remaining.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPayload);
        }
    }
}
