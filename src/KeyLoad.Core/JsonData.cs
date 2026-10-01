using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public static class JsonData
{
    public static string Validate(string json, DatabaseLimits limits, bool requireObject = true)
    {
        if (Encoding.UTF8.GetByteCount(json) > limits.MaxDocumentBytes)
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The JSON payload exceeds its byte limit.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = limits.MaxJsonDepth });
            if (requireObject && document.RootElement.ValueKind != JsonValueKind.Object)
                throw Errors.Fail(ErrorCode.Validation, "The JSON payload must be an object.");
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes)) WriteCanonical(writer, document.RootElement);
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
        catch (JsonException) { throw Errors.Fail(ErrorCode.Validation, "The JSON payload is invalid or too deeply nested."); }
    }

    public static string Fingerprint<T>(T value)
    {
        using var document = JsonDocument.Parse(JsonDefaults.Serialize(value));
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes)) WriteCanonical(writer, document.RootElement, decimalNumbers: false);
        return Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element, bool decimalNumbers = true)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = element.EnumerateObject().ToArray();
                if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                    throw Errors.Fail(ErrorCode.Validation, "Duplicate JSON property names are not allowed.");
                writer.WriteStartObject();
                foreach (var property in properties.OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value, decimalNumbers);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item, decimalNumbers);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                if (!decimalNumbers) { element.WriteTo(writer); break; }
                if (!element.TryGetDecimal(out var number))
                    throw Errors.Fail(ErrorCode.Validation, "JSON numbers must fit the decimal numeric policy.");
                writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
                break;
            default: element.WriteTo(writer); break;
        }
    }

    public static object? Scalar(JsonElement document, string pointer)
    {
        var current = document;
        foreach (var part in PathSegments(pointer))
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var child)) current = child;
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index < current.GetArrayLength()) current = current[index];
            else return MissingValue.Instance;
        }
        return current.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => current.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when current.TryGetDecimal(out var number) => number,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, "This field is not a scalar value.")
        };
    }

    public static string[] PathSegments(string pointer)
    {
        if (pointer.Length == 0) return [];
        if (!pointer.StartsWith('/') || pointer.Length > 1_024)
            throw Errors.Fail(ErrorCode.Validation, "Field paths must be bounded JSON pointers.");
        var parts = pointer[1..].Split('/');
        foreach (var part in parts)
            for (var i = 0; i < part.Length; i++)
                if (part[i] == '~' && (++i == part.Length || part[i] is not ('0' or '1')))
                    throw Errors.Fail(ErrorCode.Validation, "A field path contains invalid escaping.");
        return parts.Select(p => p.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
    }

    public static string Path(params string[] segments) => "/" + string.Join('/', segments.Select(p =>
        p.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)));

    public static string Patch(string json, FieldPatch[] patches, DatabaseLimits limits)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw Errors.Fail(ErrorCode.Validation, "A document must be an object.");
        foreach (var patch in patches)
        {
            var segments = PathSegments(patch.Path);
            if (segments.Length == 0 || segments.Any(s => s == "*"))
                throw Errors.Fail(ErrorCode.Validation, "Patch paths must address concrete fields.");
            JsonNode parent = root;
            foreach (var segment in segments[..^1])
            {
                if (parent is not JsonObject obj || obj[segment] is not { } child)
                    throw Errors.Fail(ErrorCode.Validation, "A patch parent is missing or is not an object.");
                parent = child;
            }
            if (parent is not JsonObject target) throw Errors.Fail(ErrorCode.UnsupportedCapability, "Array patch operations are not supported in protocol v1.");
            var field = segments[^1];
            if (patch.Kind == PatchKind.Remove) target.Remove(field);
            else target[field] = JsonNode.Parse(Validate(patch.ValueJson ?? "null", limits, false));
        }
        return Validate(root.ToJsonString(), limits);
    }

    public static void Identifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > 256 || value.Any(char.IsControl))
            throw Errors.Fail(ErrorCode.Validation, "An identifier is missing, too long or contains control characters.");
    }
}
