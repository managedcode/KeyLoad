using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Checks independent input schemas for both shortest-path MCP contracts.</summary>
internal static class McpGraphPathInputSchemaAssertions
{
    private const string DirectTool = McpCallerProtocol.GraphShortestPath;
    private static readonly ImmutableArray<string> DirectProperties =
    [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Graph,
        McpDiscoveryProtocol.FromEntity, McpDiscoveryProtocol.To, McpDiscoveryProtocol.MaxDepth,
        McpDiscoveryProtocol.MaxVertices, McpDiscoveryProtocol.MaxEdges, McpDiscoveryProtocol.Labels];
    private static readonly ImmutableArray<string> DirectRequired =
    [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Graph,
        McpDiscoveryProtocol.FromEntity, McpDiscoveryProtocol.To];
    private static readonly ImmutableArray<string> QueryProperties =
    [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Sql, McpDiscoveryProtocol.Parameters,
        McpDiscoveryProtocol.AllowFullScan, McpDiscoveryProtocol.Cursor, McpDiscoveryProtocol.QueryDialectVersion];
    private static readonly ImmutableArray<string> QueryRequired =
    [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Sql];
    private static readonly ImmutableArray<string> EntityProperties =
    [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Collection, McpDiscoveryProtocol.Id];
    private static readonly ImmutableArray<string> PartitionRequired =
    [McpDiscoveryProtocol.TenantId, McpDiscoveryProtocol.DatabaseId,
        McpDiscoveryProtocol.TransactionDomainId, McpDiscoveryProtocol.PartitionKey];
    private static readonly ImmutableArray<string> PartitionProperties =
        PartitionRequired.Add(McpDiscoveryProtocol.AtomicPartitionId);

    internal static async Task VerifyAsync(string toolName, JsonElement request, JsonElement root)
    {
        if (toolName == DirectTool)
        {
            await VerifyDirectAsync(request, root);
        }
        else
        {
            await VerifySqlAsync(request, root);
        }
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
        {
            await VerifyTypeAsync(properties.GetProperty(field), McpDiscoveryProtocol.Integer, root);
        }
        var labels = Resolve(root, properties.GetProperty(McpDiscoveryProtocol.Labels));
        await VerifyExactTypeSetAsync(labels, root, McpDiscoveryProtocol.Array, McpDiscoveryProtocol.Null);
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
        var dialect = Resolve(root, queryProperties.GetProperty(McpDiscoveryProtocol.QueryDialectVersion));
        await VerifyTypeAsync(dialect, McpDiscoveryProtocol.Integer, root);
        await Assert.That(dialect.GetProperty(McpDiscoveryProtocol.Default).GetInt32())
            .IsEqualTo(McpDiscoveryProtocol.DefaultQueryDialectVersion);
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
        await VerifyFieldsAsync(root, schema, PartitionProperties, PartitionRequired);
        var properties = schema.GetProperty(McpDiscoveryProtocol.Properties);
        foreach (var field in PartitionRequired)
        {
            await VerifyScalarTypeAsync(properties.GetProperty(field), McpDiscoveryProtocol.String, root);
        }
        await VerifyExactTypeSetAsync(properties.GetProperty(McpDiscoveryProtocol.AtomicPartitionId), root,
            McpDiscoveryProtocol.String, McpDiscoveryProtocol.Null);
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
        if (expected == McpDiscoveryProtocol.Integer)
        {
            var type = schema.GetProperty(McpDiscoveryProtocol.Type);
            if (type.ValueKind == JsonValueKind.String)
            {
                await Assert.That(type.GetString()).IsEqualTo(McpDiscoveryProtocol.Integer);
                return;
            }
            await VerifyExactTypeSetAsync(schema, root, McpDiscoveryProtocol.String, McpDiscoveryProtocol.Integer);
            return;
        }
        await Assert.That(ContainsType(schema, expected, root)).IsTrue();
    }

    private static async Task VerifyScalarTypeAsync(JsonElement schema, string expected, JsonElement root)
    {
        schema = Resolve(root, schema);
        var type = schema.GetProperty(McpDiscoveryProtocol.Type);
        await Assert.That(type.ValueKind).IsEqualTo(JsonValueKind.String);
        await Assert.That(type.GetString()).IsEqualTo(expected);
    }

    private static bool ContainsType(JsonElement schema, string expected, JsonElement root)
        => McpGraphPathSchemaTypes.ContainsType(schema, expected, root);

    private static JsonElement FindType(JsonElement schema, string expected, JsonElement root)
        => McpGraphPathSchemaTypes.FindType(schema, expected, root);

    private static Task VerifyExactTypeSetAsync(JsonElement schema, JsonElement root, string first, string second)
        => McpGraphPathSchemaTypes.VerifyExactTypeSetAsync(schema, root, first, second);

    private static JsonElement Resolve(JsonElement root, JsonElement schema)
        => McpGraphPathSchemaTypes.Resolve(root, schema);
}
