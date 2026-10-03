using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class Neo4jQueryProtocol
{
    private const int AcceptedStatusCode = 202;
    private const int QueryErrorStatusCode = 400;
    private const int MaximumNativeCodeLength = 256;
    private const int NativeCodeComponentCount = 4;
    private const string NativeCodePrefix = "Neo";
    private const string FailurePrefix = "Neo4j:";
    private const string InvalidResponse = "Neo4j:InvalidQueryResponse";
    private const string ErrorsProperty = "errors";
    private const string CodeProperty = "code";
    private const string DataProperty = "data";
    private const string FieldsProperty = "fields";
    private const string ValuesProperty = "values";
    private const string QueryTypeProperty = "queryType";
    private const string BookmarksProperty = "bookmarks";
    private const string SchemaQueryType = "s";

    internal static void RequireQueryStatus(int statusCode)
    {
        if (statusCode is not AcceptedStatusCode and not QueryErrorStatusCode)
        {
            throw Invalid();
        }
    }

    internal static void ValidateResponse(int statusCode, JsonElement response)
    {
        RequireQueryStatus(statusCode);
        ValidateErrors(response);
        if (statusCode != AcceptedStatusCode)
        {
            throw Invalid();
        }

        ValidateSuccessEnvelope(response);
    }

    internal static void ValidateErrors(JsonElement response)
    {
        var code = ReadFirstNativeErrorCode(response);
        if (code is not null)
        {
            throw new ComparisonFailureException(FailurePrefix + code);
        }
    }

    internal static string ReadNativeErrorCode(JsonElement response) => ReadFirstNativeErrorCode(response) ?? throw Invalid();

    internal static void ValidateConstraintCreation(JsonElement response)
    {
        ValidateErrors(response);
        var data = RequiredProperty(response, DataProperty);
        var fields = RequiredProperty(data, FieldsProperty);
        var values = RequiredProperty(data, ValuesProperty);
        var queryType = RequiredProperty(response, QueryTypeProperty);
        var bookmarks = RequiredProperty(response, BookmarksProperty);
        if (fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() != 0
            || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != 0
            || queryType.ValueKind != JsonValueKind.String || queryType.GetString() != SchemaQueryType
            || !IsStringArray(bookmarks))
        {
            throw Invalid();
        }
    }

    private static void ValidateSuccessEnvelope(JsonElement response)
    {
        var data = RequiredProperty(response, DataProperty);
        var fields = RequiredProperty(data, FieldsProperty);
        var values = RequiredProperty(data, ValuesProperty);
        if (!IsStringArray(fields) || values.ValueKind != JsonValueKind.Array)
        {
            throw Invalid();
        }

        var fieldCount = fields.GetArrayLength();
        foreach (var row in values.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != fieldCount)
            {
                throw Invalid();
            }
        }
    }

    private static string? ReadFirstNativeErrorCode(JsonElement response)
    {
        if (!TryGetUniqueProperty(response, ErrorsProperty, out var errors, out var duplicate))
        {
            if (duplicate || response.ValueKind != JsonValueKind.Object)
            {
                throw Invalid();
            }

            return null;
        }

        if (errors.ValueKind != JsonValueKind.Array)
        {
            throw Invalid();
        }

        string? firstCode = null;
        foreach (var error in errors.EnumerateArray())
        {
            var code = ReadErrorCode(error);
            firstCode ??= code;
        }

        return firstCode;
    }

    private static string ReadErrorCode(JsonElement error)
    {
        var code = RequiredProperty(error, CodeProperty);
        if (code.ValueKind != JsonValueKind.String || !IsNativeCode(code.GetString()!))
        {
            throw Invalid();
        }

        return code.GetString()!;
    }

    private static JsonElement RequiredProperty(JsonElement element, string propertyName)
    {
        if (!TryGetUniqueProperty(element, propertyName, out var value, out _))
        {
            throw Invalid();
        }

        return value;
    }

    private static bool TryGetUniqueProperty(JsonElement element, string propertyName, out JsonElement value, out bool duplicate)
    {
        value = default;
        duplicate = false;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var found = false;
        foreach (var property in element.EnumerateObject())
        {
            if (!property.NameEquals(propertyName))
            {
                continue;
            }

            if (found)
            {
                duplicate = true;
                return false;
            }

            found = true;
            value = property.Value;
        }

        return found;
    }

    private static bool IsStringArray(JsonElement element) => element.ValueKind == JsonValueKind.Array
        && element.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String);

    private static bool IsNativeCode(string value)
    {
        if (value.Length > MaximumNativeCodeLength)
        {
            return false;
        }

        var parts = value.Split('.');
        return parts.Length == NativeCodeComponentCount && parts[0] == NativeCodePrefix && parts.All(IsIdentifier);
    }

    private static bool IsIdentifier(string value) => value.Length > 0 && IsAsciiLetter(value[0])
        && value.All(character => IsAsciiLetter(character) || character is >= '0' and <= '9' or '_');

    private static bool IsAsciiLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    internal static ComparisonFailureException Invalid() => new(InvalidResponse);
}
