using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class OpenSearchJson
{
    private const int LastElementOffset = 1;

    internal static JsonElement RequiredObject(JsonElement parent, string property)
        => Required(parent, property, JsonValueKind.Object);

    internal static JsonElement RequiredArray(JsonElement parent, string property)
        => Required(parent, property, JsonValueKind.Array);

    internal static string RequiredString(JsonElement parent, params string[] path)
    {
        var value = RequiredValue(parent, path);
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new ComparisonFailureException(OpenSearchNames.ExpectedString);
    }

    internal static int RequiredInt32(JsonElement parent, string property)
    {
        var value = Required(parent, property, JsonValueKind.Number);
        return value.TryGetInt32(out var number) ? number : throw new ComparisonFailureException(OpenSearchNames.ExpectedInteger);
    }

    internal static bool RequiredBoolean(JsonElement parent, string property)
    {
        var value = Required(parent, property, JsonValueKind.True, JsonValueKind.False);
        return value.GetBoolean();
    }

    internal static bool OptionalBoolean(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True;

    internal static JsonElement Required(JsonElement parent, string property, params JsonValueKind[] allowed)
    {
        if (!parent.TryGetProperty(property, out var value) || !allowed.Contains(value.ValueKind))
        {
            throw new ComparisonFailureException(allowed.Contains(JsonValueKind.Array) ? OpenSearchNames.ExpectedArray : OpenSearchNames.ExpectedObject);
        }

        return value;
    }

    internal static JsonElement RequiredPath(JsonElement parent, params string[] path)
    {
        foreach (var property in path)
        {
            parent = RequiredObject(parent, property);
        }

        return parent;
    }

    private static JsonElement RequiredValue(JsonElement parent, string[] path)
    {
        const int FirstElementIndex = 0;
        const int AdjacentElementOffset = 1;

        for (var index = FirstElementIndex; index < path.Length - AdjacentElementOffset; index++)
        {
            parent = RequiredObject(parent, path[index]);
        }

        return parent.TryGetProperty(path[^LastElementOffset], out var value) ? value
            : throw new ComparisonFailureException(OpenSearchNames.ExpectedObject);
    }
}
