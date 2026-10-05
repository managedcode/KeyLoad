using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PartitionQueryMcpSchemaAssertions
{
    private const string MaximumReferenceDepthMessage = "The partition-query schema reference is too deep.";
    private const int MaximumReferenceDepth = 8;
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
    private static readonly string[] QueryRowFields = [PartitionQueryMcpProtocol.EntityId, PartitionQueryMcpProtocol.Revision,
        PartitionQueryMcpProtocol.Json, PartitionQueryMcpProtocol.Redacted, PartitionQueryMcpProtocol.RedactedFields];

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
        await VerifyObjectAsync(Resolve(input, requestProperties.GetProperty(PartitionQueryMcpProtocol.Query)),
            [PartitionQueryMcpProtocol.Collection, PartitionQueryMcpProtocol.Alias, PartitionQueryMcpProtocol.Projection,
                PartitionQueryMcpProtocol.Filter, PartitionQueryMcpProtocol.Order, PartitionQueryMcpProtocol.Limit,
                PartitionQueryMcpProtocol.Explain, PartitionQueryMcpProtocol.ModelSource],
            [PartitionQueryMcpProtocol.Collection, PartitionQueryMcpProtocol.Alias, PartitionQueryMcpProtocol.Projection,
                PartitionQueryMcpProtocol.Filter, PartitionQueryMcpProtocol.Order, PartitionQueryMcpProtocol.Limit]);
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
        await VerifyQueryRowAsync(root, rowProperties.GetProperty(PartitionQueryMcpProtocol.Row));
        var leaves = Resolve(root, properties.GetProperty(PartitionQueryMcpProtocol.Leaves).GetProperty(PartitionQueryMcpProtocol.Items));
        await VerifyObjectAsync(leaves, WitnessFields, WitnessFields);
        await VerifyPartitionAsync(root, leaves.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(McpDiscoveryProtocol.Partition));
        foreach (var field in new[] { PartitionQueryMcpProtocol.CutPosition, PartitionQueryMcpProtocol.PolicyEpoch, PartitionQueryMcpProtocol.SchemaVersion })
        { await VerifyPrimitiveAsync(root, leaves.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(field), McpDiscoveryProtocol.Integer); }
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
        await VerifyObjectAsync(schema, EntityFields, EntityFields);
        await VerifyPartitionAsync(root, schema.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(McpDiscoveryProtocol.Partition));
        await VerifyPrimitiveAsync(root, schema.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(PartitionQueryMcpProtocol.Collection), McpDiscoveryProtocol.String);
        await VerifyPrimitiveAsync(root, schema.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(PartitionQueryMcpProtocol.Id), McpDiscoveryProtocol.String);
    }

    private static async Task VerifyQueryRowAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyObjectAsync(schema, QueryRowFields,
            [PartitionQueryMcpProtocol.EntityId, PartitionQueryMcpProtocol.Revision, PartitionQueryMcpProtocol.Json]);
        var properties = schema.GetProperty(McpDiscoveryProtocol.Properties);
        await VerifyPrimitiveAsync(root, properties.GetProperty(PartitionQueryMcpProtocol.EntityId), McpDiscoveryProtocol.String);
        await VerifyPrimitiveAsync(root, properties.GetProperty(PartitionQueryMcpProtocol.Revision), McpDiscoveryProtocol.Integer);
        await VerifyPrimitiveAsync(root, properties.GetProperty(PartitionQueryMcpProtocol.Json), McpDiscoveryProtocol.String);
        await VerifyPrimitiveAsync(root, properties.GetProperty(PartitionQueryMcpProtocol.Redacted), McpDiscoveryProtocol.Boolean);
        await VerifyStringArrayAsync(root, properties.GetProperty(PartitionQueryMcpProtocol.RedactedFields));
    }

    private static async Task VerifyPartitionAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyObjectAsync(schema, [.. PartitionFields, PartitionQueryMcpProtocol.AtomicPartitionId], PartitionFields);
        await VerifyPrimitiveAsync(root, schema.GetProperty(McpDiscoveryProtocol.Properties)
            .GetProperty(PartitionQueryMcpProtocol.AtomicPartitionId), McpDiscoveryProtocol.String);
        foreach (var field in PartitionFields)
        { await VerifyPrimitiveAsync(root, schema.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(field), McpDiscoveryProtocol.String); }
    }

    private static async Task VerifyDictionaryAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        schema = FindBranch(schema, McpDiscoveryProtocol.Object, allowNullType: true);
        await Assert.That(schema.TryGetProperty(McpDiscoveryProtocol.AdditionalProperties, out var additional)).IsTrue();
        await Assert.That(additional.ValueKind).IsNotEqualTo(JsonValueKind.False);
    }

    private static async Task VerifyStringArrayAsync(JsonElement root, JsonElement schema)
    {
        schema = FindBranch(Resolve(root, schema), McpDiscoveryProtocol.Array);
        await VerifyPrimitiveAsync(root, schema.GetProperty(McpDiscoveryProtocol.Items), McpDiscoveryProtocol.String);
    }

    private static async Task VerifyPrimitiveAsync(JsonElement root, JsonElement schema, string expected)
    {
        schema = Resolve(root, schema);
        await Assert.That(HasType(schema, expected)).IsTrue();
    }

    private static async Task VerifyObjectAsync(JsonElement schema, string[] fields, string[] required)
    {
        await Assert.That(HasType(schema, McpDiscoveryProtocol.Object)).IsTrue();
        await Assert.That(schema.GetProperty(McpDiscoveryProtocol.AdditionalProperties).ValueKind)
            .IsEqualTo(JsonValueKind.False);
        var properties = schema.GetProperty(McpDiscoveryProtocol.Properties).EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        await Assert.That(properties.SetEquals(fields)).IsTrue();
        var requiredValues = schema.GetProperty(McpDiscoveryProtocol.Required);
        var actualRequired = requiredValues.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
        await Assert.That(actualRequired.SetEquals(required)).IsTrue();
    }

    private static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        for (var depth = 0; depth < MaximumReferenceDepth && schema.TryGetProperty(McpDiscoveryProtocol.ReferenceKeyword, out var reference); depth++)
        {
            var pointer = reference.GetString();
            if (pointer is null || !pointer.StartsWith("#/", StringComparison.Ordinal))
            { throw new InvalidOperationException(MaximumReferenceDepthMessage); }
            schema = root;
            foreach (var segment in pointer[2..].Split('/'))
            { schema = schema.GetProperty(segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)); }
        }
        if (schema.TryGetProperty(McpDiscoveryProtocol.ReferenceKeyword, out _))
        { throw new InvalidOperationException(MaximumReferenceDepthMessage); }
        return schema;
    }

    private static bool HasType(JsonElement schema, string expected)
    {
        if (HasDirectType(schema, expected, allowNullType: false))
        { return true; }
        foreach (var keyword in new[] { McpDiscoveryProtocol.AnyOf, McpDiscoveryProtocol.OneOf })
        {
            if (schema.TryGetProperty(keyword, out var variants) && variants.EnumerateArray().Any(item => HasType(item, expected)))
            { return true; }
        }
        return false;
    }

    private static JsonElement FindBranch(JsonElement schema, string expectedType, bool allowNullType = false)
    {
        if (HasDirectType(schema, expectedType, allowNullType))
        { return schema; }
        foreach (var keyword in new[] { McpDiscoveryProtocol.AnyOf, McpDiscoveryProtocol.OneOf })
        {
            if (!schema.TryGetProperty(keyword, out var variants))
            { continue; }
            foreach (var variant in variants.EnumerateArray())
            {
                if (HasDirectType(variant, expectedType, allowNullType))
                { return variant; }
            }
        }
        throw new InvalidOperationException(MaximumReferenceDepthMessage);
    }

    private static bool HasDirectType(JsonElement schema, string expected, bool allowNullType)
    {
        if (!schema.TryGetProperty(McpDiscoveryProtocol.Type, out var actual))
        { return false; }
        if (actual.ValueKind == JsonValueKind.String)
        { return actual.GetString() == expected; }
        if (actual.ValueKind != JsonValueKind.Array)
        { return false; }

        var types = actual.EnumerateArray().ToArray();
        if (types.Length == 1)
        { return types[0].ValueKind == JsonValueKind.String && types[0].GetString() == expected; }
        return allowNullType && types.Length == 2
            && types.All(item => item.ValueKind == JsonValueKind.String)
            && types.Count(item => item.GetString() == expected) == 1
            && types.Count(item => item.GetString() == McpDiscoveryProtocol.Null) == 1;
    }
}
