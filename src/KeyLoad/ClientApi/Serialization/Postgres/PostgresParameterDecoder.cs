using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class PostgresParameterDecoder
{
    private const string BooleanTrueShort = "t";
    private const string BooleanTrue = "true";
    private const string BooleanFalseShort = "f";
    private const string BooleanFalse = "false";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static JsonElement Read(int oid, short format, ReadOnlySpan<byte> bytes, IOptions<McpExecutionOptions> framing)
    {
        if (format is not (PostgresProtocol.TextFormat or PostgresProtocol.BinaryFormat))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported);
        }
        try
        {
            return format == PostgresProtocol.BinaryFormat
                ? Binary(oid, bytes, framing) : Text(oid, Utf8.GetString(bytes), framing);
        }
        catch (Exception error) when (error is DecoderFallbackException or JsonException or FormatException or OverflowException)
        {
            throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid);
        }
    }

    private static JsonElement Binary(int oid, ReadOnlySpan<byte> bytes, IOptions<McpExecutionOptions> framing)
    {
        if (oid == PostgresNumericProtocol.Oid)
        {
            return Value(PostgresNumericDecimal.Binary(bytes));
        }
        if (oid == PostgresProtocol.Int16Oid && bytes.Length == sizeof(short))
        {
            return Value(BinaryPrimitives.ReadInt16BigEndian(bytes));
        }
        if (oid == PostgresProtocol.Int32Oid && bytes.Length == sizeof(int))
        {
            return Value(BinaryPrimitives.ReadInt32BigEndian(bytes));
        }
        if (oid == PostgresProtocol.Int64Oid && bytes.Length == sizeof(long))
        {
            return Value(BinaryPrimitives.ReadInt64BigEndian(bytes));
        }
        if (oid == PostgresProtocol.BooleanOid && bytes.Length == sizeof(byte))
        {
            if (bytes[PostgresProtocol.FirstField] > PostgresProtocol.JsonbVersion)
            {
                throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid);
            }
            return Value(bytes[PostgresProtocol.FirstField] == PostgresProtocol.JsonbVersion);
        }
        if (oid is PostgresProtocol.Int16Oid or PostgresProtocol.Int32Oid or PostgresProtocol.Int64Oid or PostgresProtocol.BooleanOid)
        {
            throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid);
        }
        if (oid == PostgresProtocol.TextOid)
        {
            return Value(Utf8.GetString(bytes));
        }
        if (oid == PostgresProtocol.JsonOid)
        {
            return Json(bytes, framing);
        }
        if (oid == PostgresProtocol.JsonbOid && !bytes.IsEmpty && bytes[PostgresProtocol.FirstField] == PostgresProtocol.JsonbVersion)
        {
            return Json(bytes[sizeof(byte)..], framing);
        }
        if (oid == PostgresProtocol.JsonbOid)
        {
            throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid);
        }
        throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported);
    }

    private static JsonElement Text(int oid, string text, IOptions<McpExecutionOptions> framing)
        => oid switch
        {
            PostgresNumericProtocol.Oid => Value(PostgresNumericDecimal.Text(text)),
            PostgresProtocol.Int16Oid => Value(short.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
            PostgresProtocol.Int32Oid => Value(int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
            PostgresProtocol.Int64Oid => Value(long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
            PostgresProtocol.BooleanOid => Boolean(text),
            PostgresProtocol.TextOid => Value(text),
            PostgresProtocol.JsonOid or PostgresProtocol.JsonbOid => Json(Utf8.GetBytes(text), framing),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported)
        };

    private static JsonElement Boolean(string text)
        => text switch
        {
            BooleanTrueShort or BooleanTrue => Value(true),
            BooleanFalseShort or BooleanFalse => Value(false),
            _ => throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid)
        };

    private static JsonElement Json(ReadOnlySpan<byte> bytes, IOptions<McpExecutionOptions> framing)
    {
        _ = McpFrameBounds.InspectValue(bytes, framing.Value.MaximumCanonicalPayloadBytes, framing);
        using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = framing.Value.MaximumDepth });
        return document.RootElement.Clone();
    }

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, JsonDefaults.Options);
}
