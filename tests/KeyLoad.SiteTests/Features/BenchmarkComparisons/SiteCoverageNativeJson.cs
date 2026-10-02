using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteCoverageNativeJson
{
    public static void RequireObject(JsonElement value, IReadOnlySet<string> required, IReadOnlySet<string> optional)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(SiteCoverageTokens.JsonFailure);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw Invalid(SiteCoverageTokens.DuplicateFieldFailure);
            }

            if (!required.Contains(property.Name) && !optional.Contains(property.Name))
            {
                throw Invalid(SiteCoverageTokens.JsonFailure);
            }
        }

        if (required.Any(field => !seen.Contains(field)))
        {
            throw Invalid(SiteCoverageTokens.JsonFailure);
        }
    }

    public static string GetString(JsonElement value, string field)
    {
        var property = value.GetProperty(field);
        return property.ValueKind == JsonValueKind.String && property.GetString() is { } text
            ? text : throw Invalid(SiteCoverageTokens.JsonFailure);
    }

    public static int GetInt32(JsonElement value, string field)
    {
        var property = value.GetProperty(field);
        return property.TryGetInt32(out var number) ? number : throw Invalid(SiteCoverageTokens.JsonFailure);
    }

    public static long GetInt64(JsonElement value, string field)
    {
        var property = value.GetProperty(field);
        return property.TryGetInt64(out var number) ? number : throw Invalid(SiteCoverageTokens.JsonFailure);
    }

    public static InvalidDataException Invalid(string message) => new(message);

    internal static class NativeFields
    {
        public static readonly IReadOnlySet<string> NoOptional = new HashSet<string>(StringComparer.Ordinal);
        public static readonly IReadOnlySet<string> ScriptRequired = Set(SiteCoverageTokens.ScriptId, SiteCoverageTokens.Url,
            SiteCoverageTokens.Functions);
        public static readonly IReadOnlySet<string> ScriptOptional = Set(SiteCoverageTokens.ExecutionContextId);
        public static readonly IReadOnlySet<string> FunctionRequired = Set(SiteCoverageTokens.FunctionName,
            SiteCoverageTokens.IsBlockCoverage, SiteCoverageTokens.Ranges);
        public static readonly IReadOnlySet<string> RangeRequired = Set(SiteCoverageTokens.StartOffset,
            SiteCoverageTokens.EndOffset, SiteCoverageTokens.Count);

        private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);
    }
}
