using System.Globalization;
using System.Text.Json;

namespace KeyLoad.Core.ResourceExecution;

/// <summary>Writes the existing canonical JSON ordering and numeric representation incrementally.</summary>
internal static class CanonicalJsonWriter
{
    private const int FlushPendingBytes = 65_536;
    private const int FirstOrdinal = 1;
    private const int AdjacentElementOffset = 1;

    private const string DuplicateProperty = "Duplicate JSON property names are not allowed.";
    private const string DecimalRequired = "JSON numbers must fit the decimal numeric policy.";
    private const string DecimalFormat = "G29";

    /// <summary>Checks content policy without sorting properties or encoding output that the caller discards.</summary>
    /// <param name="element">Parsed JSON within its document lifetime.</param>
    internal static void Validate(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw Errors.Fail(ErrorCode.Validation, DuplicateProperty);
                    }
                    Validate(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Validate(item);
                }
                break;
            case JsonValueKind.Number:
                if (!element.TryGetDecimal(out _))
                {
                    throw Errors.Fail(ErrorCode.Validation, DecimalRequired);
                }
                break;
        }
    }

    /// <summary>Writes a complete canonical JSON value without retaining its complete output.</summary>
    /// <param name="writer">Caller-owned UTF-8 writer and destination.</param>
    /// <param name="element">Parsed JSON value within its document lifetime.</param>
    /// <param name="decimalNumbers">Normalize decimals for validation; preserve raw spelling for fingerprints.</param>
    internal static void Write(Utf8JsonWriter writer, JsonElement element, bool decimalNumbers = true)
    {
        ArgumentNullException.ThrowIfNull(writer) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
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
