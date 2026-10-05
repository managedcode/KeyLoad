using System.Collections.Immutable;
using System.Text.Json;
namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Independently freezes direct and SQL shortest-path tool schemas.</summary>
internal static class McpGraphPathSchemaAssertions
{
    private const string DirectTool = McpCallerProtocol.GraphShortestPath;
    private const string SqlTool = McpCallerProtocol.QueryGraphPath;
    private const int MaximumReferenceDepth = 8;
    private const string InvalidTool = "The MCP graph-path tool name is not recognized.";
    private const string InvalidReference = "The MCP path schema reference is not local.";
    private const string ExcessReferenceDepth = "The MCP path schema reference is too deep.";
    private static readonly ImmutableArray<string> ResultProperties =
    [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Found, McpDiscoveryProtocol.Hops,
        McpDiscoveryProtocol.Vertices, McpDiscoveryProtocol.Edges, McpDiscoveryProtocol.CutPosition];
    private static readonly ImmutableArray<string> EntityProperties =
    [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Collection, McpDiscoveryProtocol.Id];
    private static readonly ImmutableArray<string> PartitionRequired =
    [McpDiscoveryProtocol.TenantId, McpDiscoveryProtocol.DatabaseId,
        McpDiscoveryProtocol.TransactionDomainId, McpDiscoveryProtocol.PartitionKey];
    private static readonly ImmutableArray<string> PartitionProperties =
        PartitionRequired.Add(McpDiscoveryProtocol.AtomicPartitionId);
    private static readonly ImmutableArray<string> EdgeProperties =
    [McpDiscoveryProtocol.Id, McpDiscoveryProtocol.FromEntity, McpDiscoveryProtocol.To,
        McpDiscoveryProtocol.Label, McpDiscoveryProtocol.AttributesJson, McpDiscoveryProtocol.Revision];

    internal static async Task VerifyAsync(string toolName, JsonElement input, JsonElement output,
        ImmutableArray<string> requiredResult)
    {
        var request = Resolve(input, input.GetProperty(McpDiscoveryProtocol.Definitions).GetProperty(McpCallerProtocol.Request));
        if (toolName != DirectTool && toolName != SqlTool)
        { throw new InvalidOperationException(InvalidTool); }
        await McpGraphPathInputSchemaAssertions.VerifyAsync(toolName, request, input);
        var result = Resolve(output, output.GetProperty(McpDiscoveryProtocol.Definitions).GetProperty(McpCallerProtocol.Result));
        await VerifyFieldsAsync(result, ResultProperties, requiredResult);
        await VerifyPrimitiveAsync(result, McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Integer, output);
        await VerifyPrimitiveAsync(result, McpDiscoveryProtocol.Found, McpDiscoveryProtocol.Boolean, output);
        await VerifyNullableIntegerAsync(result.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(McpDiscoveryProtocol.Hops), output);
        await VerifyPrimitiveAsync(result, McpDiscoveryProtocol.CutPosition, McpDiscoveryProtocol.Integer, output);
        await VerifyEntityArrayAsync(result, output);
        await VerifyEdgeArrayAsync(result, output);
    }

    private static async Task VerifyEntityArrayAsync(JsonElement result, JsonElement root)
    {
        var vertices = result.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(McpDiscoveryProtocol.Vertices);
        await Assert.That(vertices.GetProperty(McpDiscoveryProtocol.Type).GetString()).IsEqualTo(McpDiscoveryProtocol.Array);
        var entity = Resolve(root, vertices.GetProperty(McpDiscoveryProtocol.Items));
        await VerifyEntityAsync(entity, root);
    }

    private static async Task VerifyEdgeArrayAsync(JsonElement result, JsonElement root)
    {
        var edges = result.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(McpDiscoveryProtocol.Edges);
        await Assert.That(edges.GetProperty(McpDiscoveryProtocol.Type).GetString()).IsEqualTo(McpDiscoveryProtocol.Array);
        var edge = Resolve(root, edges.GetProperty(McpDiscoveryProtocol.Items));
        await VerifyFieldsAsync(edge, EdgeProperties, EdgeProperties);
        await VerifyPrimitiveAsync(edge, McpDiscoveryProtocol.Id, McpDiscoveryProtocol.String, root);
        await VerifyPrimitiveAsync(edge, McpDiscoveryProtocol.Label, McpDiscoveryProtocol.String, root);
        await VerifyPrimitiveAsync(edge, McpDiscoveryProtocol.AttributesJson, McpDiscoveryProtocol.String, root);
        await VerifyPrimitiveAsync(edge, McpDiscoveryProtocol.Revision, McpDiscoveryProtocol.Integer, root);
        var properties = edge.GetProperty(McpDiscoveryProtocol.Properties);
        foreach (var name in new[] { McpDiscoveryProtocol.FromEntity, McpDiscoveryProtocol.To })
        {
            await VerifyEntityAsync(properties.GetProperty(name), root);
        }
    }

    private static async Task VerifyEntityAsync(JsonElement schema, JsonElement root)
    {
        schema = Resolve(root, schema);
        await VerifyFieldsAsync(schema, EntityProperties, EntityProperties);
        var properties = schema.GetProperty(McpDiscoveryProtocol.Properties);
        var partition = Resolve(root, properties.GetProperty(McpDiscoveryProtocol.Partition));
        await VerifyFieldsAsync(partition, PartitionProperties, PartitionRequired);
        foreach (var field in PartitionProperties)
        { await VerifyPrimitiveAsync(partition, field, McpDiscoveryProtocol.String, root); }
        await VerifyPrimitiveAsync(schema, McpDiscoveryProtocol.Collection, McpDiscoveryProtocol.String, root);
        await VerifyPrimitiveAsync(schema, McpDiscoveryProtocol.Id, McpDiscoveryProtocol.String, root);
    }

    private static async Task VerifyFieldsAsync(JsonElement schema, ImmutableArray<string> properties,
        ImmutableArray<string> required)
    {
        await VerifyPropertiesAsync(schema, properties);
        await RequiredAsync(schema, required);
    }

    private static async Task VerifyPropertiesAsync(JsonElement schema, ImmutableArray<string> expected)
    {
        await Assert.That(schema.GetProperty(McpDiscoveryProtocol.Type).GetString()).IsEqualTo(McpDiscoveryProtocol.Object);
        await Assert.That(schema.GetProperty(McpDiscoveryProtocol.AdditionalProperties).GetBoolean()).IsFalse();
        var actual = schema.GetProperty(McpDiscoveryProtocol.Properties).EnumerateObject()
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        await Assert.That(actual.SetEquals(expected)).IsTrue();
    }

    private static async Task RequiredAsync(JsonElement schema, ImmutableArray<string> expected)
    {
        var actual = schema.GetProperty(McpDiscoveryProtocol.Required).EnumerateArray()
            .Select(field => field.GetString()!).ToHashSet(StringComparer.Ordinal);
        await Assert.That(actual.SetEquals(expected)).IsTrue();
    }

    private static async Task VerifyPrimitiveAsync(JsonElement owner, string field, string expectedType, JsonElement root)
    {
        var property = Resolve(root, owner.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(field));
        await Assert.That(property.GetProperty(McpDiscoveryProtocol.Type).GetString()).IsEqualTo(expectedType);
    }

    private static async Task VerifyNullableIntegerAsync(JsonElement schema, JsonElement root)
    {
        schema = Resolve(root, schema);
        await Assert.That(ContainsType(schema, McpDiscoveryProtocol.Null, root)).IsTrue();
        await Assert.That(ContainsType(schema, McpDiscoveryProtocol.Integer, root)).IsTrue();
    }

    private static bool ContainsType(JsonElement schema, string expected, JsonElement root)
    {
        schema = Resolve(root, schema);
        if (schema.TryGetProperty(McpDiscoveryProtocol.Type, out var type))
        {
            if (type.ValueKind == JsonValueKind.String && type.GetString() == expected)
            { return true; }
            if (type.ValueKind == JsonValueKind.Array && type.EnumerateArray()
                .Any(value => value.GetString() == expected))
            { return true; }
        }
        foreach (var keyword in new[] { McpDiscoveryProtocol.AnyOf, McpDiscoveryProtocol.OneOf })
        {
            if (schema.TryGetProperty(keyword, out var variants) && variants.ValueKind == JsonValueKind.Array
                && variants.EnumerateArray().Any(variant => ContainsType(variant, expected, root)))
            { return true; }
        }
        return false;
    }

    private static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        for (var depth = 0; depth < MaximumReferenceDepth
            && schema.TryGetProperty(McpDiscoveryProtocol.ReferenceKeyword, out var reference); depth++)
        {
            var pointer = reference.GetString();
            if (pointer is null || !pointer.StartsWith("#/", StringComparison.Ordinal))
            { throw new InvalidOperationException(InvalidReference); }
            schema = root;
            foreach (var segment in pointer[2..].Split('/'))
            {
                var name = segment.Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);
                schema = schema.GetProperty(name);
            }
        }
        if (schema.TryGetProperty(McpDiscoveryProtocol.ReferenceKeyword, out _))
        { throw new InvalidOperationException(ExcessReferenceDepth); }
        return schema;
    }
}
