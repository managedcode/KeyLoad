using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class Neo4jQueryResponse
{
    private const string InvalidResponse = Neo4jHarnessConstants.InvalidQueryResponse;

    public static void ValidateErrors(JsonElement response)
    {
        if (!TryGetUniqueProperty(response, Neo4jHarnessConstants.ErrorsProperty, out var errors, out var duplicate))
        {
            if (duplicate || response.ValueKind != JsonValueKind.Object)
            {
                throw Invalid();
            }

            return;
        }

        if (errors.ValueKind != JsonValueKind.Array)
        {
            throw Invalid();
        }

        var codes = errors.EnumerateArray().Select(ReadErrorCode).ToArray();
        if (codes.Length > 0)
        {
            throw new ComparisonFailureException(Neo4jHarnessConstants.Neo4jPrefix + codes[0]);
        }
    }

    public static void ValidateConstraintCreation(JsonElement response)
    {
        ValidateErrors(response);
        var data = RequiredProperty(response, Neo4jHarnessConstants.DataProperty);
        var fields = RequiredProperty(data, Neo4jHarnessConstants.FieldsProperty);
        var values = RequiredProperty(data, Neo4jHarnessConstants.ValuesProperty);
        var queryType = RequiredProperty(response, Neo4jHarnessConstants.QueryTypeProperty);
        var bookmarks = RequiredProperty(response, Neo4jHarnessConstants.BookmarksProperty);
        if (fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() != 0
            || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != 0
            || queryType.ValueKind != JsonValueKind.String || queryType.GetString() != "s"
            || !IsStringArray(bookmarks))
        {
            throw Invalid();
        }
    }

    private static string ReadErrorCode(JsonElement error)
    {
        var code = RequiredProperty(error, Neo4jHarnessConstants.ErrorCodeProperty);
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
        var parts = value.Split('.');
        return parts.Length == 4 && parts[0] == Neo4jHarnessConstants.NativeCodePrefix
            && parts.All(IsIdentifier);
    }

    private static bool IsIdentifier(string value) => value.Length > 0 && IsAsciiLetter(value[0])
        && value.All(character => IsAsciiLetter(character) || character is >= '0' and <= '9' or '_');

    private static bool IsAsciiLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static ComparisonFailureException Invalid() => new(InvalidResponse);
}
