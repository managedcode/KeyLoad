using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Validates canonical JSON and builds stable operation fingerprints and scalar paths.</summary>
public static class JsonData
{
    private const string UnsupportedScalarFieldDetail = "This field is not a scalar value.";
    private const string PayloadLimitDetail = "The JSON payload exceeds its byte limit.";
    private const string ObjectRequiredDetail = "The JSON payload must be an object.";
    private const string InvalidPayloadDetail = "The JSON payload is invalid or too deeply nested.";
    private const string InvalidDocumentDetail = "A document must be an object.";
    private const string ConcretePatchDetail = "Patch paths must address concrete fields.";
    private const string MissingPatchParentDetail = "A patch parent is missing or is not an object.";
    private const string UnsupportedArrayPatchDetail = "Array patch operations are not supported in protocol v1.";
    private const string InvalidIdentifierDetail = "An identifier is missing, too long or contains control characters.";
    private const string WildcardPathSegment = "*";
    private const string NullJsonValue = "null";
    private const int MaximumIdentifierBytes = 256;

    /// <summary>Returns canonical JSON while enforcing input size, nesting and decimal-number policy.</summary>
    /// <param name="json">Owned input JSON text.</param>
    /// <param name="limits">Input byte and nesting limits.</param>
    /// <param name="requireObject">Whether the root must be an object.</param>
    /// <returns>Owned canonical text with ordinal property order.</returns>
    public static string Validate(string json, DatabaseLimits limits, bool requireObject = true)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(limits);
        if (Encoding.UTF8.GetByteCount(json) > limits.MaxDocumentBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, PayloadLimitDetail);
        }
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = limits.MaxJsonDepth });
            if (requireObject && document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Errors.Fail(ErrorCode.Validation, ObjectRequiredDetail);
            }
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                CanonicalJsonWriter.Write(writer, document.RootElement);
            }
            return Encoding.UTF8.GetString(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length)));
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPayloadDetail);
        }
    }

    /// <summary>Hashes canonical serializer output without normalizing numeric spelling.</summary>
    /// <typeparam name="T">The value's serialization type.</typeparam>
    /// <param name="value">Value or null serialized with the database's canonical options.</param>
    /// <returns>The exact lowercase SHA-256 fingerprint.</returns>
    public static string Fingerprint<T>(T value)
    {
        using var document = JsonSerializer.SerializeToDocument(value, JsonDefaults.Options);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var bytes = new CanonicalHashStream(hash);
        using (var writer = new Utf8JsonWriter(bytes))
        {
            CanonicalJsonWriter.Write(writer, document.RootElement, decimalNumbers: false);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Reads a scalar at a validated JSON pointer.</summary>
    /// <param name="document">JSON root inside its owning document lifetime.</param>
    /// <param name="pathText">JSON pointer to a scalar, or empty text for the root.</param>
    /// <returns>An owned scalar, null, or the distinct missing-value marker.</returns>
    public static object? Scalar(JsonElement document, string pathText)
        => Scalar(document, PathSegments(pathText));

    /// <summary>Reads a scalar with path segments prepared once by the operation.</summary>
    /// <param name="document">JSON root inside its owning document lifetime.</param>
    /// <param name="pathSegments">Decoded path segments produced by <see cref="PathSegments"/>.</param>
    /// <returns>An owned scalar, null, or the distinct missing-value marker.</returns>
    public static object? Scalar(JsonElement document, ReadOnlySpan<string> pathSegments)
    {
        var current = document;
        foreach (var part in pathSegments)
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var child))
            {
                current = child;
            }
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index < current.GetArrayLength())
            {
                current = current[index];
            }
            else
            {
                return MissingValue.Instance;
            }
        }
        return current.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => current.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when current.TryGetDecimal(out var number) => number,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedScalarFieldDetail)
        };
    }

    /// <summary>Decodes a bounded JSON pointer into owned reusable path segments.</summary>
    /// <param name="pathText">RFC 6901 pointer, or empty text for the root.</param>
    /// <returns>Decoded path segments in traversal order.</returns>
    public static string[] PathSegments(string pathText) => JsonPointerPaths.Parse(pathText);

    /// <summary>Encodes concrete path segments with the existing JSON pointer escaping.</summary>
    /// <param name="segments">Owned unescaped path segments.</param>
    /// <returns>A slash-prefixed JSON pointer.</returns>
    public static string Path(params string[] segments) => JsonPointerPaths.Encode(segments);

    /// <summary>Applies field patches in order and returns canonical validated JSON.</summary>
    /// <param name="json">Existing object JSON.</param>
    /// <param name="patches">Validated concrete field patches.</param>
    /// <param name="limits">Input and resulting document limits.</param>
    /// <returns>Owned canonical object JSON after all patches.</returns>
    public static string Patch(string json, IReadOnlyList<FieldPatch> patches, DatabaseLimits limits)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(patches);
        ArgumentNullException.ThrowIfNull(limits);
        var root = JsonNode.Parse(json) as JsonObject ?? throw Errors.Fail(ErrorCode.Validation, InvalidDocumentDetail);
        foreach (var patch in patches)
        {
            var segments = PathSegments(patch.Path);
            if (segments.Length == 0 || segments.Any(segment => segment == WildcardPathSegment))
            {
                throw Errors.Fail(ErrorCode.Validation, ConcretePatchDetail);
            }
            JsonNode parent = root;
            foreach (var segment in segments[..^1])
            {
                if (parent is not JsonObject obj || obj[segment] is not { } child)
                {
                    throw Errors.Fail(ErrorCode.Validation, MissingPatchParentDetail);
                }
                parent = child;
            }
            if (parent is not JsonObject target)
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedArrayPatchDetail);
            }
            var field = segments[^1];
            if (patch.Kind == PatchKind.Remove)
            {
                target.Remove(field);
            }
            else
            {
                target[field] = JsonNode.Parse(Validate(patch.ValueJson ?? NullJsonValue, limits, false));
            }
        }
        return Validate(root.ToJsonString(), limits);
    }

    /// <summary>Rejects missing, oversized or control-containing database identifiers.</summary>
    /// <param name="value">Identifier text to validate.</param>
    public static void Identifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > MaximumIdentifierBytes || value.Any(char.IsControl))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidIdentifierDetail);
        }
    }
}
