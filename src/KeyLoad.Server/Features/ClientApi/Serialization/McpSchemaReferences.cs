using System.Collections.Frozen;
using System.Text.Json.Nodes;

namespace KeyLoad.Server;

/// <summary>Relocates native exporter local references only through JSON Schema positions.</summary>
internal static class McpSchemaReferences
{
    private static readonly FrozenSet<string> Maps =
        new[] { McpSchemaKeywords.Properties, McpSchemaKeywords.Definitions, McpSchemaKeywords.PatternProperties,
            McpSchemaKeywords.DependentSchemas }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> Arrays =
        new[] { McpSchemaKeywords.AnyOf, McpSchemaKeywords.OneOf, McpSchemaKeywords.AllOf,
            McpSchemaKeywords.PrefixItems }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> Singles =
        new[] { McpSchemaKeywords.Items, McpSchemaKeywords.AdditionalProperties, McpSchemaKeywords.Contains,
            McpSchemaKeywords.Not, McpSchemaKeywords.If, McpSchemaKeywords.Then, McpSchemaKeywords.Else,
            McpSchemaKeywords.PropertyNames, McpSchemaKeywords.UnevaluatedProperties, McpSchemaKeywords.UnevaluatedItems,
            McpSchemaKeywords.ContentSchema }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Rebases one exported graph; default/const/enum/examples remain untouched application data.</summary>
    /// <param name="schema">Native exported schema graph to relocate.</param>
    /// <param name="prefix">Root-local pointer at which the graph will be embedded.</param>
    internal static void Rebase(JsonNode schema, string prefix)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (schema is not JsonObject node)
        { return; }
        RewriteReference(node, prefix);
        foreach (var property in node)
        {
            RewriteKeyword(property.Key, property.Value, prefix);
        }
    }

    private static void RewriteKeyword(string keyword, JsonNode? value, string prefix)
    {
        if (Maps.Contains(keyword))
        { RewriteMap(value, prefix); return; }
        if (Arrays.Contains(keyword))
        { RewriteArray(value, prefix); return; }
        if (Singles.Contains(keyword) && value is { } child)
        { Rebase(child, prefix); }
    }

    private static void RewriteReference(JsonObject node, string prefix)
    {
        if (node[McpSchemaKeywords.Reference] is not { } reference)
        { return; }
        var pointer = reference.GetValue<string>();
        if (pointer == McpSchemaKeywords.ReferenceRoot)
        {
            node[McpSchemaKeywords.Reference] = prefix;
        }
        else if (pointer.StartsWith(McpSchemaKeywords.LocalReferencePrefix, StringComparison.Ordinal))
        {
            node[McpSchemaKeywords.Reference] = string.Concat(prefix.AsSpan(), pointer.AsSpan(McpSchemaKeywords.ReferenceRoot.Length));
        }
        else
        { throw new InvalidOperationException(McpCatalogProtocol.InvalidSchemaReference); }
    }

    private static void RewriteMap(JsonNode? value, string prefix)
    {
        if (value is not JsonObject map)
        { return; }
        foreach (var property in map)
        {
            if (property.Value is { } child)
            { Rebase(child, prefix); }
        }
    }

    private static void RewriteArray(JsonNode? value, string prefix)
    {
        if (value is not JsonArray array)
        { return; }
        foreach (var child in array)
        {
            if (child is not null)
            { Rebase(child, prefix); }
        }
    }
}
