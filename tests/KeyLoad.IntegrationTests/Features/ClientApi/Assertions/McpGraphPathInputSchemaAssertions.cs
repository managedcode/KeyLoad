using System.Text.Json;
using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Checks independent input schemas for both shortest-path MCP contracts.</summary>
internal static class McpGraphPathInputSchemaAssertions
{
    private const string DirectTool = McpCallerProtocol.GraphShortestPath;
    private const string ReferenceKeyword = "$ref";
    private const int MaximumReferenceDepth = 8;
    private const string InvalidSchemaType = "The accepted path schema type is missing.";
    private static readonly ImmutableArray<string> DirectProperties =
    [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Graph,
        McpDiscoveryProtocol.FromEntity, McpDiscoveryProtocol.To, McpDiscoveryProtocol.MaxDepth,
        McpDiscoveryProtocol.MaxVertices, McpDiscoveryProtocol.MaxEdges, McpDiscoveryProtocol.Labels];
    private static readonly ImmutableArray<string> DirectRequired =
    [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Graph,
        McpDiscoveryProtocol.FromEntity, McpDiscoveryProtocol.To];
    private static readonly ImmutableArray<string> QueryProperties =
    [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Sql, McpDiscoveryProtocol.Parameters,
        McpDiscoveryProtocol.AllowFullScan, McpDiscoveryProtocol.Cursor];
    private static readonly ImmutableArray<string> QueryRequired =
    [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Sql];
    private static readonly ImmutableArray<string> EntityProperties =
    [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Collection, McpDiscoveryProtocol.Id];
    private static readonly ImmutableArray<string> PartitionProperties =
    [McpDiscoveryProtocol.TenantId, McpDiscoveryProtocol.DatabaseId,
        McpDiscoveryProtocol.TransactionDomainId, McpDiscoveryProtocol.PartitionKey];

    internal static async Task VerifyAsync(string toolName, JsonElement request, JsonElement root)
    {
        if (toolName == DirectTool)
        { await VerifyDirectAsync(request, root); }
        else
        { await VerifySqlAsync(request, root); }
    }

    private static async Task VerifyDirectAsync(JsonElement request, JsonElement root)
    {
        await VerifyFieldsAsync(root, request, DirectProperties, DirectRequired);
        var properties = request.GetProperty(McpDiscoveryProtocol.Properties);
        await VerifyTypeAsync(properties.GetProperty(McpDiscoveryProtocol.Version), McpDiscoveryProtocol.Integer, root);
        await VerifyTypeAsync(properties.GetProperty(McpDiscoveryProtocol.Graph), McpDiscoveryProtocol.String, root);
        await VerifyPartitionAsync(properties.GetProperty(McpDiscoveryProtocol.Partition), root);
        await VerifyEntityAsync(properties.GetProperty(McpDiscoveryProtocol.FromEntity), root);
        await VerifyEntityAsync(properties.GetProperty(McpDiscoveryProtocol.To), root);
        foreach (var field in new[] { McpDiscoveryProtocol.MaxDepth, McpDiscoveryProtocol.MaxVertices, McpDiscoveryProtocol.MaxEdges })
        { await VerifyTypeAsync(properties.GetProperty(field), McpDiscoveryProtocol.Integer, root); }
        var labels = Resolve(root, properties.GetProperty(McpDiscoveryProtocol.Labels));
        await Assert.That(ContainsType(labels, McpDiscoveryProtocol.Null, root)).IsTrue();
        await Assert.That(ContainsType(labels, McpDiscoveryProtocol.Array, root)).IsTrue();
        var array = FindType(labels, McpDiscoveryProtocol.Array, root);
        await VerifyTypeAsync(array.GetProperty(McpDiscoveryProtocol.Items), McpDiscoveryProtocol.String, root);
    }

    private static async Task VerifySqlAsync(JsonElement request, JsonElement root)
    {
        await VerifyFieldsAsync(root, request, [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Query],
            [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Query]);
        var properties = request.GetProperty(McpDiscoveryProtocol.Properties);
        await VerifyTypeAsync(properties.GetProperty(McpDiscoveryProtocol.Version), McpDiscoveryProtocol.Integer, root);
        var query = Resolve(root, properties.GetProperty(McpDiscoveryProtocol.Query));
        await VerifyFieldsAsync(root, query, QueryProperties, QueryRequired);
        var queryProperties = query.GetProperty(McpDiscoveryProtocol.Properties);
        await VerifyPartitionAsync(queryProperties.GetProperty(McpDiscoveryProtocol.Partition), root);
        await VerifyTypeAsync(queryProperties.GetProperty(McpDiscoveryProtocol.Sql), McpDiscoveryProtocol.String, root);
        await VerifyTypeAsync(queryProperties.GetProperty(McpDiscoveryProtocol.Parameters), McpDiscoveryProtocol.Object, root);
        await Assert.That(ContainsType(queryProperties.GetProperty(McpDiscoveryProtocol.Parameters),
            McpDiscoveryProtocol.Null, root)).IsTrue();
        await VerifyTypeAsync(queryProperties.GetProperty(McpDiscoveryProtocol.AllowFullScan), McpDiscoveryProtocol.Boolean, root);
        await VerifyNullableStringAsync(queryProperties.GetProperty(McpDiscoveryProtocol.Cursor), root);
    }

    private static async Task VerifyEntityAsync(JsonElement schema, JsonElement root)
    {
        schema = Resolve(root, schema);
        await VerifyFieldsAsync(root, schema, EntityProperties, EntityProperties);
        var properties = schema.GetProperty(McpDiscoveryProtocol.Properties);
        await VerifyPartitionAsync(properties.GetProperty(McpDiscoveryProtocol.Partition), root);
        await VerifyTypeAsync(properties.GetProperty(McpDiscoveryProtocol.Collection), McpDiscoveryProtocol.String, root);
        await VerifyTypeAsync(properties.GetProperty(McpDiscoveryProtocol.Id), McpDiscoveryProtocol.String, root);
    }

    private static async Task VerifyPartitionAsync(JsonElement schema, JsonElement root)
    {
        schema = Resolve(root, schema);
        await VerifyFieldsAsync(root, schema, PartitionProperties, PartitionProperties);
        var properties = schema.GetProperty(McpDiscoveryProtocol.Properties);
        foreach (var field in PartitionProperties)
        { await VerifyTypeAsync(properties.GetProperty(field), McpDiscoveryProtocol.String, root); }
    }

    private static async Task VerifyNullableStringAsync(JsonElement schema, JsonElement root)
    {
        schema = Resolve(root, schema);
        await Assert.That(ContainsType(schema, McpDiscoveryProtocol.Null, root)).IsTrue();
        await Assert.That(ContainsType(schema, McpDiscoveryProtocol.String, root)).IsTrue();
    }

    private static async Task VerifyFieldsAsync(JsonElement root, JsonElement schema, ImmutableArray<string> properties,
        ImmutableArray<string> required)
    {
        schema = Resolve(root, schema);
        await Assert.That(schema.GetProperty(McpDiscoveryProtocol.Type).GetString()).IsEqualTo(McpDiscoveryProtocol.Object);
        await Assert.That(schema.GetProperty(McpDiscoveryProtocol.AdditionalProperties).GetBoolean()).IsFalse();
        var actualProperties = schema.GetProperty(McpDiscoveryProtocol.Properties).EnumerateObject()
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        var actualRequired = schema.GetProperty(McpDiscoveryProtocol.Required).EnumerateArray()
            .Select(property => property.GetString()!).ToHashSet(StringComparer.Ordinal);
        await Assert.That(actualProperties.SetEquals(properties)).IsTrue();
        await Assert.That(actualRequired.SetEquals(required)).IsTrue();
    }

    private static async Task VerifyTypeAsync(JsonElement schema, string expected, JsonElement root)
    {
        schema = Resolve(root, schema);
        await Assert.That(ContainsType(schema, expected, root)).IsTrue();
    }

    private static bool ContainsType(JsonElement schema, string expected, JsonElement root)
    {
        schema = Resolve(root, schema);
        if (schema.TryGetProperty(McpDiscoveryProtocol.Type, out var type)
            && ((type.ValueKind == JsonValueKind.String && type.GetString() == expected)
                || (type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(item => item.GetString() == expected))))
        { return true; }
        foreach (var keyword in new[] { McpDiscoveryProtocol.AnyOf, McpDiscoveryProtocol.OneOf })
        {
            if (schema.TryGetProperty(keyword, out var variants) && variants.ValueKind == JsonValueKind.Array
                && variants.EnumerateArray().Any(variant => ContainsType(variant, expected, root)))
            { return true; }
        }
        return false;
    }

    private static JsonElement FindType(JsonElement schema, string expected, JsonElement root)
    {
        schema = Resolve(root, schema);
        if (schema.TryGetProperty(McpDiscoveryProtocol.Type, out var type)
            && type.ValueKind == JsonValueKind.String && type.GetString() == expected)
        { return schema; }
        foreach (var keyword in new[] { McpDiscoveryProtocol.AnyOf, McpDiscoveryProtocol.OneOf })
        {
            if (!schema.TryGetProperty(keyword, out var variants) || variants.ValueKind != JsonValueKind.Array)
            { continue; }
            foreach (var variant in variants.EnumerateArray())
            {
                if (ContainsType(variant, expected, root))
                { return FindType(variant, expected, root); }
            }
        }
        throw new InvalidOperationException(InvalidSchemaType);
    }

    private static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        for (var depth = 0; depth < MaximumReferenceDepth && schema.TryGetProperty(ReferenceKeyword, out var reference); depth++)
        {
            var pointer = reference.GetString();
            if (pointer is null || !pointer.StartsWith("#/", StringComparison.Ordinal))
            { throw new InvalidOperationException("The path schema reference is not local."); }
            schema = root;
            foreach (var segment in pointer[2..].Split('/'))
            { schema = schema.GetProperty(segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)); }
        }
        if (schema.TryGetProperty(ReferenceKeyword, out _))
        { throw new InvalidOperationException("The path schema reference is too deep."); }
        return schema;
    }
}
