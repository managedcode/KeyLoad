using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Writes the existing canonical JSON ordering and numeric representation incrementally.</summary>
internal static class CanonicalJsonWriter
{
    private const int FirstOrdinal = 1;
    private const int AdjacentElementOffset = 1;

    private const string DuplicateProperty = "Duplicate JSON property names are not allowed.";
    private const string DecimalRequired = "JSON numbers must fit the decimal numeric policy.";
    private const string DecimalFormat = "G29";

    /// <summary>Writes a complete canonical JSON value without retaining its complete output.</summary>
    /// <param name="writer">Caller-owned UTF-8 writer and destination.</param>
    /// <param name="element">Parsed JSON value within its document lifetime.</param>
    /// <param name="decimalNumbers">Normalize decimals for validation; preserve raw spelling for fingerprints.</param>
    internal static void Write(Utf8JsonWriter writer, JsonElement element, bool decimalNumbers = true)
        => Write(writer, element, SerializationExecutionRegistration.Process, decimalNumbers);

    /// <summary>Writes canonical bytes using one validated native process policy snapshot.</summary>
    /// <param name="writer">Caller-owned UTF-8 writer and destination.</param>
    /// <param name="element">Parsed JSON value within its document lifetime.</param>
    /// <param name="executionOptions">The actual native serializer execution wrapper.</param>
    /// <param name="decimalNumbers">Whether decimal normalization is required.</param>
    internal static void Write(Utf8JsonWriter writer, JsonElement element,
        IOptions<SerializationExecutionOptions> executionOptions, bool decimalNumbers = true)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(executionOptions);
        var configured = executionOptions.Value;
        configured.Validate();
        WriteValue(writer, element, decimalNumbers, configured.CanonicalJsonFlushPendingBytes);
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonElement element, bool decimalNumbers, int flushPendingBytes)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(writer, element, decimalNumbers, flushPendingBytes);
                break;
            case JsonValueKind.Array:
                WriteArray(writer, element, decimalNumbers, flushPendingBytes);
                break;
            case JsonValueKind.Number:
                WriteNumber(writer, element, decimalNumbers);
                break;
            default:
                element.WriteTo(writer);
                break;
        }
        if (writer.BytesPending >= flushPendingBytes)
        {
            writer.Flush();
        }
    }

    private static void WriteObject(Utf8JsonWriter writer, JsonElement element, bool decimalNumbers, int flushPendingBytes)
    {
        var properties = element.EnumerateObject()
            .Select(property => new NamedProperty(property.Name, property.Value)).ToArray();
        Array.Sort(properties, static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        for (var index = FirstOrdinal; index < properties.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(properties[index - AdjacentElementOffset].Name, properties[index].Name))
            {
                throw Errors.Fail(ErrorCode.Validation, DuplicateProperty);
            }
        }
        writer.WriteStartObject();
        foreach (var property in properties)
        {
            writer.WritePropertyName(property.Name);
            WriteValue(writer, property.Value, decimalNumbers, flushPendingBytes);
        }
        writer.WriteEndObject();
    }

    private static void WriteArray(Utf8JsonWriter writer, JsonElement element, bool decimalNumbers, int flushPendingBytes)
    {
        writer.WriteStartArray();
        foreach (var item in element.EnumerateArray())
        {
            WriteValue(writer, item, decimalNumbers, flushPendingBytes);
        }
        writer.WriteEndArray();
    }

    private static void WriteNumber(Utf8JsonWriter writer, JsonElement element, bool decimalNumbers)
    {
        if (!decimalNumbers)
        {
            element.WriteTo(writer);
            return;
        }
        if (!element.TryGetDecimal(out var number))
        {
            throw Errors.Fail(ErrorCode.Validation, DecimalRequired);
        }
        writer.WriteRawValue(number.ToString(DecimalFormat, CultureInfo.InvariantCulture));
    }

    private readonly record struct NamedProperty(string Name, JsonElement Value);
}
