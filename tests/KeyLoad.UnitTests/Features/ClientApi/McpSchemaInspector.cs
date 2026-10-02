using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Inspects actual exported JSON schema documents and resolves their local pointers.</summary>
internal static class McpSchemaInspector
{
    internal const string Defs = "$defs";
    internal const string Properties = "properties";
    internal const string Type = "type";
    internal const string AnyOf = "anyOf";
    internal const string OneOf = "oneOf";
    internal const string Required = "required";
    internal const string Default = "default";
    internal const string Examples = "examples";
    internal const string Ref = "$ref";
    internal const string Kind = "kind";
    internal const string Const = "const";
    internal const string Items = "items";
    internal const string Enum = "enum";
    internal const string Null = "null";
    internal const string Array = "array";
    internal const string Integer = "integer";
    internal const string String = "string";
    internal const string Result = "result";
    internal const string Error = "error";
    internal const string RequestId = "requestId";
    private const string PointerRoot = "#";
    private const string EscapedSlash = "~1";
    private const string EscapedTilde = "~0";
    private const string Slash = "/";
    private const string Tilde = "~";
    private const char PointerSeparator = '/';

    internal static JsonElement DefinedRequest(JsonElement schema) =>
        schema.GetProperty(Defs).GetProperty(McpCanonicalTestData.RequestKey);

    internal static ImmutableArray<string> References(JsonElement schema)
    {
        var result = ImmutableArray.CreateBuilder<string>();
        Collect(schema, result);
        return result.ToImmutable();
    }

    private static void Collect(JsonElement node, ImmutableArray<string>.Builder result)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
            { Collect(item, result); }
            return;
        }
        if (node.ValueKind != JsonValueKind.Object)
        { return; }
        foreach (var property in node.EnumerateObject())
        {
            if (property.Name is Default or Examples)
            { continue; }
            if (property.Name == Ref)
            { result.Add(property.Value.GetString()!); }
            else
            { Collect(property.Value, result); }
        }
    }

    internal static JsonElement Resolve(JsonElement root, string pointer)
    {
        if (!pointer.StartsWith(PointerRoot, StringComparison.Ordinal))
        { throw new InvalidOperationException(pointer); }
        var current = root;
        foreach (var segment in pointer.AsSpan(PointerRoot.Length).ToString().Split(PointerSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var key = segment.Replace(EscapedSlash, Slash, StringComparison.Ordinal).Replace(EscapedTilde, Tilde, StringComparison.Ordinal);
            current = current.ValueKind == JsonValueKind.Array
                ? current[int.Parse(key, CultureInfo.InvariantCulture)] : current.GetProperty(key);
        }
        return current;
    }

    internal static bool HasType(JsonElement schema, string type)
    {
        if (schema.TryGetProperty(Type, out var types))
        {
            return types.ValueKind == JsonValueKind.String ? types.GetString() == type :
                types.EnumerateArray().Any(value => value.GetString() == type);
        }
        return schema.TryGetProperty(AnyOf, out var branches) && branches.EnumerateArray().Any(branch => HasType(branch, type));
    }

    internal static ImmutableArray<string> Discriminators(JsonElement schema) =>
        [.. schema.GetProperty(AnyOf).EnumerateArray().Select(branch =>
            branch.GetProperty(Properties).GetProperty(Kind).GetProperty(Const).GetString()!)];
}
