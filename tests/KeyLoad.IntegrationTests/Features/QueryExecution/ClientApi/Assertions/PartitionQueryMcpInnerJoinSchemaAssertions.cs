using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Checks additive nullable Q2 join and source-identity fields in the existing MCP schema.</summary>
internal static class PartitionQueryMcpInnerJoinSchemaAssertions
{
    private const string Object = "object";
    private const string Null = "null";
    private const string String = "string";
    private const string Integer = "integer";
    private const string Boolean = "boolean";
    private const string Array = "array";
    private static readonly string[] QueryRowFields = [PartitionQueryMcpProtocol.EntityId, PartitionQueryMcpProtocol.Revision,
        PartitionQueryMcpProtocol.Json, PartitionQueryMcpProtocol.Redacted, PartitionQueryMcpProtocol.RedactedFields,
        PartitionQueryMcpProtocol.Sources];
    private static readonly string[] SourceFields = [PartitionQueryMcpProtocol.Alias, PartitionQueryMcpProtocol.EntityId,
        PartitionQueryMcpProtocol.Revision];

    internal static async Task VerifyQueryRowAsync(JsonElement root, JsonElement schema)
    {
        schema = PartitionQueryMcpSchemaAssertions.Resolve(root, schema);
        await PartitionQueryMcpSchemaAssertions.VerifyObjectAsync(schema, QueryRowFields,
            [PartitionQueryMcpProtocol.EntityId, PartitionQueryMcpProtocol.Revision, PartitionQueryMcpProtocol.Json]);
        var properties = schema.GetProperty(PartitionQueryMcpProtocol.Properties);
        await PartitionQueryMcpSchemaAssertions.VerifyPrimitiveAsync(root,
            properties.GetProperty(PartitionQueryMcpProtocol.EntityId), String);
        await PartitionQueryMcpSchemaAssertions.VerifyPrimitiveAsync(root,
            properties.GetProperty(PartitionQueryMcpProtocol.Revision), Integer);
        await PartitionQueryMcpSchemaAssertions.VerifyPrimitiveAsync(root,
            properties.GetProperty(PartitionQueryMcpProtocol.Json), String);
        await PartitionQueryMcpSchemaAssertions.VerifyPrimitiveAsync(root,
            properties.GetProperty(PartitionQueryMcpProtocol.Redacted), Boolean);
        await PartitionQueryMcpSchemaAssertions.VerifyStringArrayAsync(root,
            properties.GetProperty(PartitionQueryMcpProtocol.RedactedFields));
        await VerifySourceArrayAsync(root, properties.GetProperty(PartitionQueryMcpProtocol.Sources));
    }

    internal static async Task VerifyProjectionAsync(JsonElement root, JsonElement schema)
    {
        schema = PartitionQueryMcpSchemaAssertions.Resolve(root, schema);
        var item = PartitionQueryMcpSchemaAssertions.Resolve(root,
            schema.GetProperty(PartitionQueryMcpProtocol.Items));
        var fields = new[] { PartitionQueryMcpProtocol.Path, PartitionQueryMcpProtocol.Alias,
            PartitionQueryMcpProtocol.SourceAlias };
        await PartitionQueryMcpSchemaAssertions.VerifyObjectAsync(item, fields,
            [PartitionQueryMcpProtocol.Path, PartitionQueryMcpProtocol.Alias]);
        var properties = item.GetProperty(PartitionQueryMcpProtocol.Properties);
        await PartitionQueryMcpSchemaAssertions.VerifyPrimitiveAsync(root,
            properties.GetProperty(PartitionQueryMcpProtocol.Path), String);
        await PartitionQueryMcpSchemaAssertions.VerifyPrimitiveAsync(root,
            properties.GetProperty(PartitionQueryMcpProtocol.Alias), String);
        await VerifyNullableStringAsync(root, properties.GetProperty(PartitionQueryMcpProtocol.SourceAlias));
    }

    internal static async Task VerifyInnerJoinAsync(JsonElement root, JsonElement schema)
    {
        schema = PartitionQueryMcpSchemaAssertions.Resolve(root, schema);
        await VerifyNullableObjectAsync(root, schema);
        var join = FindObjectBranch(root, schema);
        var fields = new[] { PartitionQueryMcpProtocol.Collection, PartitionQueryMcpProtocol.Alias,
            PartitionQueryMcpProtocol.LeftKeyPath, PartitionQueryMcpProtocol.RightKeyPath };
        await PartitionQueryMcpSchemaAssertions.VerifyObjectShapeAsync(join, fields, fields);
        var properties = join.GetProperty(PartitionQueryMcpProtocol.Properties);
        foreach (var field in fields)
        {
            await PartitionQueryMcpSchemaAssertions.VerifyPrimitiveAsync(root, properties.GetProperty(field), String);
        }
    }

    private static async Task VerifySourceArrayAsync(JsonElement root, JsonElement schema)
    {
        schema = PartitionQueryMcpSchemaAssertions.Resolve(root, schema);
        await PartitionQueryMcpSchemaAssertions.VerifyExactTypeSetAsync(root, schema, Array, Null);
        var item = PartitionQueryMcpSchemaAssertions.Resolve(root,
            schema.GetProperty(PartitionQueryMcpProtocol.Items));
        await PartitionQueryMcpSchemaAssertions.VerifyObjectAsync(item, SourceFields, SourceFields);
        var properties = item.GetProperty(PartitionQueryMcpProtocol.Properties);
        await PartitionQueryMcpSchemaAssertions.VerifyPrimitiveAsync(root,
            properties.GetProperty(PartitionQueryMcpProtocol.Alias), String);
        await PartitionQueryMcpSchemaAssertions.VerifyPrimitiveAsync(root,
            properties.GetProperty(PartitionQueryMcpProtocol.EntityId), String);
        await PartitionQueryMcpSchemaAssertions.VerifyPrimitiveAsync(root,
            properties.GetProperty(PartitionQueryMcpProtocol.Revision), Integer);
    }

    private static async Task VerifyNullableStringAsync(JsonElement root, JsonElement schema)
    {
        schema = PartitionQueryMcpSchemaAssertions.Resolve(root, schema);
        if (schema.TryGetProperty(PartitionQueryMcpProtocol.AnyOf, out var variants))
        {
            var resolved = variants.EnumerateArray()
                .Select(item => PartitionQueryMcpSchemaAssertions.Resolve(root, item)).ToArray();
            await Assert.That(resolved.Length).IsEqualTo(2);
            await Assert.That(resolved.Count(item => PartitionQueryMcpSchemaAssertions.HasType(item, String))).IsEqualTo(1);
            await Assert.That(resolved.Count(item => PartitionQueryMcpSchemaAssertions.HasType(item, Null))).IsEqualTo(1);
            return;
        }
        await PartitionQueryMcpSchemaAssertions.VerifyExactTypeSetAsync(root, schema, String, Null);
    }

    private static async Task VerifyNullableObjectAsync(JsonElement root, JsonElement schema)
    {
        if (schema.TryGetProperty(PartitionQueryMcpProtocol.AnyOf, out var variants))
        {
            var resolved = variants.EnumerateArray()
                .Select(item => PartitionQueryMcpSchemaAssertions.Resolve(root, item)).ToArray();
            await Assert.That(resolved.Length).IsEqualTo(2);
            await Assert.That(resolved.Count(item => PartitionQueryMcpSchemaAssertions.HasType(item, Object))).IsEqualTo(1);
            await Assert.That(resolved.Count(item => PartitionQueryMcpSchemaAssertions.HasType(item, Null))).IsEqualTo(1);
            return;
        }
        await PartitionQueryMcpSchemaAssertions.VerifyExactTypeSetAsync(root, schema, Object, Null);
    }

    private static JsonElement FindObjectBranch(JsonElement root, JsonElement schema)
    {
        if (schema.TryGetProperty(PartitionQueryMcpProtocol.AnyOf, out var variants))
        {
            foreach (var variant in variants.EnumerateArray())
            {
                var resolved = PartitionQueryMcpSchemaAssertions.Resolve(root, variant);
                if (PartitionQueryMcpSchemaAssertions.HasType(resolved, Object))
                {
                    return resolved;
                }
            }
        }
        return schema;
    }
}
