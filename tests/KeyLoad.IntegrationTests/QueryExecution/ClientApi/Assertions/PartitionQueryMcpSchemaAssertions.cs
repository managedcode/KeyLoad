using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PartitionQueryMcpSchemaAssertions
{
    private static readonly string[] PartitionFields = [McpDiscoveryProtocol.TenantId, McpDiscoveryProtocol.DatabaseId,
        McpDiscoveryProtocol.TransactionDomainId, McpDiscoveryProtocol.PartitionKey];
    private static readonly string[] RequestFields = [PartitionQueryMcpProtocol.Version, PartitionQueryMcpProtocol.Partitions,
        PartitionQueryMcpProtocol.Query, PartitionQueryMcpProtocol.Parameters, PartitionQueryMcpProtocol.AllowFullScan,
        PartitionQueryMcpProtocol.AstVersion];
    private static readonly string[] PageFields = [PartitionQueryMcpProtocol.Version, PartitionQueryMcpProtocol.Rows,
        PartitionQueryMcpProtocol.Leaves, PartitionQueryMcpProtocol.Complete];
    private static readonly string[] RowFields = [PartitionQueryMcpProtocol.Reference, PartitionQueryMcpProtocol.Row];
    private static readonly string[] WitnessFields = [McpDiscoveryProtocol.Partition, PartitionQueryMcpProtocol.CutPosition,
        PartitionQueryMcpProtocol.PolicyEpoch, PartitionQueryMcpProtocol.SchemaVersion, PartitionQueryMcpProtocol.AccessPath];
    private static readonly string[] EntityFields = [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Collection,
        McpDiscoveryProtocol.Id];

    internal static async Task VerifyAsync(JsonElement input, JsonElement output)
    {
        await VerifyObjectAsync(input, [PartitionQueryMcpProtocol.Request], [PartitionQueryMcpProtocol.Request]);
        var request = Resolve(input, input.GetProperty(PartitionQueryMcpProtocol.Definitions).GetProperty(PartitionQueryMcpProtocol.Request));
        await VerifyObjectAsync(request, RequestFields, RequestFields);
        var requestProperties = request.GetProperty(McpDiscoveryProtocol.Properties);
        await VerifyPrimitiveAsync(input, requestProperties.GetProperty(PartitionQueryMcpProtocol.Version), McpDiscoveryProtocol.Integer);
        await VerifyPrimitiveAsync(input, requestProperties.GetProperty(PartitionQueryMcpProtocol.AstVersion), McpDiscoveryProtocol.Integer);
        await VerifyPrimitiveAsync(input, requestProperties.GetProperty(PartitionQueryMcpProtocol.AllowFullScan), McpDiscoveryProtocol.Boolean);
        await VerifyPartitionArrayAsync(input, requestProperties.GetProperty(PartitionQueryMcpProtocol.Partitions));
        var query = Resolve(input, requestProperties.GetProperty(PartitionQueryMcpProtocol.Query));
        await VerifyObjectAsync(query,
            [PartitionQueryMcpProtocol.Collection, PartitionQueryMcpProtocol.Alias, PartitionQueryMcpProtocol.Projection,
                PartitionQueryMcpProtocol.Filter, PartitionQueryMcpProtocol.Order, PartitionQueryMcpProtocol.Limit,
                PartitionQueryMcpProtocol.Explain, PartitionQueryMcpProtocol.ModelSource, PartitionQueryMcpProtocol.InnerJoin,
                PartitionQueryMcpProtocol.Offset],
            [PartitionQueryMcpProtocol.Collection, PartitionQueryMcpProtocol.Alias, PartitionQueryMcpProtocol.Projection,
                PartitionQueryMcpProtocol.Filter, PartitionQueryMcpProtocol.Order, PartitionQueryMcpProtocol.Limit]);
        var queryFields = query.GetProperty(McpDiscoveryProtocol.Properties);
        await VerifyPrimitiveAsync(input, queryFields.GetProperty(PartitionQueryMcpProtocol.Offset), McpDiscoveryProtocol.Integer);
        await PartitionQueryMcpInnerJoinSchemaAssertions.VerifyProjectionAsync(input,
            queryFields.GetProperty(PartitionQueryMcpProtocol.Projection));
        await PartitionQueryMcpInnerJoinSchemaAssertions.VerifyInnerJoinAsync(input,
            queryFields.GetProperty(PartitionQueryMcpProtocol.InnerJoin));
        await VerifyDictionaryAsync(input, requestProperties.GetProperty(PartitionQueryMcpProtocol.Parameters));
        await VerifyPageAsync(output);
    }

    private static async Task VerifyPageAsync(JsonElement root)
    {
        var page = Resolve(root, root.GetProperty(PartitionQueryMcpProtocol.Definitions).GetProperty(PartitionQueryMcpProtocol.Result));
        await VerifyObjectAsync(page, PageFields, PageFields);
        var properties = page.GetProperty(McpDiscoveryProtocol.Properties);
        await VerifyPrimitiveAsync(root, properties.GetProperty(PartitionQueryMcpProtocol.Version), McpDiscoveryProtocol.Integer);
        await VerifyPrimitiveAsync(root, properties.GetProperty(PartitionQueryMcpProtocol.Complete), McpDiscoveryProtocol.Boolean);
        var rows = Resolve(root, properties.GetProperty(PartitionQueryMcpProtocol.Rows).GetProperty(PartitionQueryMcpProtocol.Items));
        await VerifyObjectAsync(rows, RowFields, RowFields);
        var rowProperties = rows.GetProperty(McpDiscoveryProtocol.Properties);
        await VerifyEntityAsync(root, rowProperties.GetProperty(PartitionQueryMcpProtocol.Reference));
        await PartitionQueryMcpInnerJoinSchemaAssertions.VerifyQueryRowAsync(root,
            rowProperties.GetProperty(PartitionQueryMcpProtocol.Row));
        var leaves = Resolve(root, properties.GetProperty(PartitionQueryMcpProtocol.Leaves).GetProperty(PartitionQueryMcpProtocol.Items));
        await VerifyObjectAsync(leaves, WitnessFields, WitnessFields);
        await VerifyPartitionAsync(root, leaves.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(McpDiscoveryProtocol.Partition));
        foreach (var field in new[] { PartitionQueryMcpProtocol.CutPosition, PartitionQueryMcpProtocol.PolicyEpoch, PartitionQueryMcpProtocol.SchemaVersion })
        {
            await VerifyPrimitiveAsync(root, leaves.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(field), McpDiscoveryProtocol.Integer);
        }
        await VerifyPrimitiveAsync(root, leaves.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(PartitionQueryMcpProtocol.AccessPath), McpDiscoveryProtocol.String);
    }

    private static async Task VerifyPartitionArrayAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await Assert.That(schema.GetProperty(McpDiscoveryProtocol.Type).GetString()).IsEqualTo(McpDiscoveryProtocol.Array);
        await VerifyPartitionAsync(root, schema.GetProperty(McpDiscoveryProtocol.Items));
    }

    private static async Task VerifyEntityAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyObjectAsync(schema, [.. EntityFields, PartitionQueryMcpProtocol.Lifetime], EntityFields);
        await VerifyPartitionAsync(root, schema.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(McpDiscoveryProtocol.Partition));
        await VerifyPrimitiveAsync(root, schema.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(PartitionQueryMcpProtocol.Collection), McpDiscoveryProtocol.String);
        await VerifyPrimitiveAsync(root, schema.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(PartitionQueryMcpProtocol.Id), McpDiscoveryProtocol.String);
        await VerifyLifetimeAsync(root, schema.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(PartitionQueryMcpProtocol.Lifetime));
    }

    private static async Task VerifyLifetimeAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyExactTypeSetAsync(root, schema, McpDiscoveryProtocol.Object, McpDiscoveryProtocol.Null);
        await VerifyObjectShapeAsync(schema, [PartitionQueryMcpProtocol.Incarnation], [PartitionQueryMcpProtocol.Incarnation]);
        await VerifyPrimitiveAsync(root, schema.GetProperty(McpDiscoveryProtocol.Properties)
            .GetProperty(PartitionQueryMcpProtocol.Incarnation), McpDiscoveryProtocol.String);
    }

    private static async Task VerifyPartitionAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyObjectAsync(schema, [.. PartitionFields, PartitionQueryMcpProtocol.AtomicPartitionId], PartitionFields);
        await VerifyExactTypeSetAsync(root, schema.GetProperty(McpDiscoveryProtocol.Properties)
            .GetProperty(PartitionQueryMcpProtocol.AtomicPartitionId), McpDiscoveryProtocol.String, McpDiscoveryProtocol.Null);
        foreach (var field in PartitionFields)
        {
            await VerifyPrimitiveAsync(root, schema.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(field), McpDiscoveryProtocol.String);
        }
    }

    private static async Task VerifyDictionaryAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        schema = FindBranch(schema, McpDiscoveryProtocol.Object, allowNullType: true);
        if (schema.TryGetProperty(McpDiscoveryProtocol.AdditionalProperties, out var additional))
        {
            await Assert.That(additional.ValueKind is JsonValueKind.True or JsonValueKind.Object).IsTrue();
        }
    }

    internal static async Task VerifyStringArrayAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyExactTypeSetAsync(root, schema, McpDiscoveryProtocol.Array, McpDiscoveryProtocol.Null);
        await VerifyPrimitiveAsync(root, schema.GetProperty(McpDiscoveryProtocol.Items), McpDiscoveryProtocol.String);
    }

    internal static async Task VerifyPrimitiveAsync(JsonElement root, JsonElement schema, string expected)
    {
        schema = Resolve(root, schema);
        await Assert.That(HasType(schema, expected)).IsTrue();
    }

    internal static async Task VerifyObjectAsync(JsonElement schema, string[] fields, string[] required)
    {
        await Assert.That(HasType(schema, McpDiscoveryProtocol.Object)).IsTrue();
        await VerifyObjectShapeAsync(schema, fields, required);
    }

    internal static async Task VerifyObjectShapeAsync(JsonElement schema, string[] fields, string[] required)
    {
        await Assert.That(schema.GetProperty(McpDiscoveryProtocol.AdditionalProperties).ValueKind)
            .IsEqualTo(JsonValueKind.False);
        var properties = schema.GetProperty(McpDiscoveryProtocol.Properties).EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        await Assert.That(properties.SetEquals(fields)).IsTrue();
        var requiredValues = schema.GetProperty(McpDiscoveryProtocol.Required);
        var actualRequired = requiredValues.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
        await Assert.That(actualRequired.SetEquals(required)).IsTrue();
    }

    internal static JsonElement Resolve(JsonElement root, JsonElement schema)
        => PartitionQueryMcpSchemaTypes.Resolve(root, schema);

    internal static bool HasType(JsonElement schema, string expected)
        => PartitionQueryMcpSchemaTypes.HasType(schema, expected);

    private static JsonElement FindBranch(JsonElement schema, string expectedType, bool allowNullType = false)
        => PartitionQueryMcpSchemaTypes.FindBranch(schema, expectedType, allowNullType);

    internal static Task VerifyExactTypeSetAsync(JsonElement root, JsonElement schema, string first, string second)
        => PartitionQueryMcpSchemaTypes.VerifyExactTypeSetAsync(root, schema, first, second);
}
