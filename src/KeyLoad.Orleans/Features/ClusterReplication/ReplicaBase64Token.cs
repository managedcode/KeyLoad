using System.Buffers.Text;
using System.Text.Json;

namespace KeyLoad.Orleans;

internal static class ReplicaBase64Token
{
    private const byte Escape = (byte)'\\';
    private const byte UnicodeEscape = (byte)'u';
    private const byte Slash = (byte)'/';
    private const byte Plus = (byte)'+';
    private const byte Padding = (byte)'=';
    private const byte FirstUpper = (byte)'A';
    private const byte LastUpper = (byte)'Z';
    private const byte FirstLower = (byte)'a';
    private const byte LastLower = (byte)'z';
    private const byte FirstDigit = (byte)'0';
    private const byte LastDigit = (byte)'9';
    private const byte Space = (byte)' ';
    private const byte Tab = (byte)'\t';
    private const byte CarriageReturn = (byte)'\r';
    private const byte LineFeed = (byte)'\n';
    private const byte TabEscape = (byte)'t';
    private const byte CarriageReturnEscape = (byte)'r';
    private const byte LineFeedEscape = (byte)'n';
    private const int HexDigits = 4;
    private const char HexFormat = 'x';
    private const int MaximumAscii = 127;
    private const int Quartet = 4;
    private const int DecodedQuartetBytes = 3;
    private const int MaximumPadding = 2;

    internal static int Length(ref Utf8JsonReader reader)
    {
        ReplicaPayloadReader.Require(reader.TokenType == JsonTokenType.String);
        if (reader.ValueIsEscaped)
        {
            return EscapedLength(reader.ValueSpan);
        }

        ReplicaPayloadReader.Require(Base64.IsValid(reader.ValueSpan, out var length));
        return length;
    }

    private static int EscapedLength(ReadOnlySpan<byte> source)
    {
        var characters = 0;
        var padding = 0;
        for (var position = 0; position < source.Length;)
        {
            var value = ReadAscii(source, ref position);
            if (value is Space or Tab or CarriageReturn or LineFeed)
            {
                continue;
            }

            characters++;
            if (value == Padding)
            {
                padding++;
                ReplicaPayloadReader.Require(padding <= MaximumPadding);
                continue;
            }

            ReplicaPayloadReader.Require(padding == 0 && Alphabet(value));
        }

        ReplicaPayloadReader.Require(characters % Quartet == 0);
        return characters / Quartet * DecodedQuartetBytes - padding;
    }

    private static byte ReadAscii(ReadOnlySpan<byte> source, ref int position)
    {
        var value = source[position++];
        if (value != Escape)
        {
            return value;
        }

        ReplicaPayloadReader.Require(position < source.Length);
        var escaped = source[position++];
        if (escaped == UnicodeEscape)
        {
            ReplicaPayloadReader.Require(source.Length - position >= HexDigits);
            var hexadecimal = source.Slice(position, HexDigits);
            ReplicaPayloadReader.Require(Utf8Parser.TryParse(hexadecimal, out ushort code, out var consumed, HexFormat)
                && consumed == HexDigits && code <= MaximumAscii);
            position += HexDigits;
            return (byte)code;
        }

        return escaped switch
        {
            Slash => Slash,
            TabEscape => Tab,
            CarriageReturnEscape => CarriageReturn,
            LineFeedEscape => LineFeed,
            _ => throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload)
        };
    }

    private static bool Alphabet(byte value) => value is >= FirstUpper and <= LastUpper or >= FirstLower and <= LastLower
        or >= FirstDigit and <= LastDigit or Slash or Plus;
}
