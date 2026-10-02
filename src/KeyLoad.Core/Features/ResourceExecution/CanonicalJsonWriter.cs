using System.Globalization;
using System.Text.Json;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Writes the existing canonical JSON ordering and numeric representation incrementally.</summary>
internal static class CanonicalJsonWriter
{
    private const string DuplicateProperty = "Duplicate JSON property names are not allowed.";
    private const string DecimalRequired = "JSON numbers must fit the decimal numeric policy.";
    private const string DecimalFormat = "G29";
    private const int FlushPendingBytes = 65_536;

    /// <summary>Writes a complete canonical JSON value without retaining its complete output.</summary>
    /// <param name="writer">Caller-owned UTF-8 writer and destination.</param>
    /// <param name="element">Parsed JSON value within its document lifetime.</param>
    /// <param name="decimalNumbers">Normalize decimals for validation; preserve raw spelling for fingerprints.</param>
    internal static void Write(Utf8JsonWriter writer, JsonElement element, bool decimalNumbers = true)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteValue(writer, element, decimalNumbers);
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonElement element, bool decimalNumbers)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(writer, element, decimalNumbers);
                break;
            case JsonValueKind.Array:
                WriteArray(writer, element, decimalNumbers);
                break;
            case JsonValueKind.Number:
                WriteNumber(writer, element, decimalNumbers);
                break;
            default:
                element.WriteTo(writer);
                break;
        }
        if (writer.BytesPending >= FlushPendingBytes)
        {
            writer.Flush();
        }
    }

    private static void WriteObject(Utf8JsonWriter writer, JsonElement element, bool decimalNumbers)
    {
        var properties = element.EnumerateObject()
            .Select(property => new NamedProperty(property.Name, property.Value)).ToArray();
        Array.Sort(properties, static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        for (var index = 1; index < properties.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(properties[index - 1].Name, properties[index].Name))
            {
                throw Errors.Fail(ErrorCode.Validation, DuplicateProperty);
            }
        }
        writer.WriteStartObject();
        foreach (var property in properties)
        {
            writer.WritePropertyName(property.Name);
            WriteValue(writer, property.Value, decimalNumbers);
        }
        writer.WriteEndObject();
    }

    private static void WriteArray(Utf8JsonWriter writer, JsonElement element, bool decimalNumbers)
    {
        writer.WriteStartArray();
        foreach (var item in element.EnumerateArray())
        {
            WriteValue(writer, item, decimalNumbers);
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
