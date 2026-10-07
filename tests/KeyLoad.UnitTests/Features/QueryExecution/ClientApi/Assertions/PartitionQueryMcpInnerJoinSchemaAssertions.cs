using System.Text.Json;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Checks the native nullable Q2 source and join fields in the unit MCP catalog.</summary>
internal static class PartitionQueryMcpInnerJoinSchemaAssertions
{
    private const string ObjectType = PartitionQueryMcpProtocol.ObjectType;
    private const string Collection = PartitionQueryMcpProtocol.Collection;
    private const string Alias = PartitionQueryMcpProtocol.Alias;
    private const string EntityId = PartitionQueryMcpProtocol.EntityId;
    private const string Revision = PartitionQueryMcpProtocol.Revision;
    private const string StringType = McpSchemaInspector.String;
    private const string IntegerType = McpSchemaInspector.Integer;
    private const string ArrayType = McpSchemaInspector.Array;
    private const string NullType = McpSchemaInspector.Null;
    private const string MissingExpectedBranchMessage = "The nullable MCP schema has no expected value branch.";

    internal static async Task VerifyProjectionAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyPrimitiveAsync(root, schema, ArrayType);
        var item = Resolve(root, schema.GetProperty(McpSchemaInspector.Items));
        var fields = new[] { PartitionQueryMcpProtocol.Path, Alias, PartitionQueryMcpProtocol.SourceAlias };
        await VerifyObjectAsync(item, fields, [PartitionQueryMcpProtocol.Path, Alias]);
        var properties = item.GetProperty(McpSchemaInspector.Properties);
        await VerifyPrimitiveAsync(root, properties.GetProperty(PartitionQueryMcpProtocol.Path), StringType);
        await VerifyPrimitiveAsync(root, properties.GetProperty(Alias), StringType);
        await VerifyNullableAsync(root, properties.GetProperty(PartitionQueryMcpProtocol.SourceAlias), StringType);
    }

    internal static async Task VerifyInnerJoinAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyNullableAsync(root, schema, ObjectType);
        var join = FindTypeBranch(root, schema, ObjectType);
        var fields = new[] { Collection, Alias, PartitionQueryMcpProtocol.LeftKeyPath,
            PartitionQueryMcpProtocol.RightKeyPath };
        await VerifyObjectShapeAsync(join, fields, fields);
        var properties = join.GetProperty(McpSchemaInspector.Properties);
        foreach (var field in fields)
        { await VerifyPrimitiveAsync(root, properties.GetProperty(field), StringType); }
    }

    internal static async Task VerifySourcesAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyNullableAsync(root, schema, ArrayType);
        var array = FindTypeBranch(root, schema, ArrayType);
        var item = Resolve(root, array.GetProperty(McpSchemaInspector.Items));
        var fields = new[] { Alias, EntityId, Revision };
        await VerifyObjectAsync(item, fields, fields);
        var properties = item.GetProperty(McpSchemaInspector.Properties);
        await VerifyPrimitiveAsync(root, properties.GetProperty(Alias), StringType);
        await VerifyPrimitiveAsync(root, properties.GetProperty(EntityId), StringType);
        await VerifyIntegerAsync(root, properties.GetProperty(Revision));
    }

    private static async Task VerifyNullableAsync(JsonElement root, JsonElement schema, string valueType)
    {
        schema = Resolve(root, schema);
        if (schema.TryGetProperty(McpSchemaInspector.AnyOf, out var variants))
        {
            var branches = variants.EnumerateArray().Select(item => Resolve(root, item)).ToArray();
            await Assert.That(branches.Length).IsEqualTo(2);
            await Assert.That(branches.Count(item => HasExactType(item, valueType))).IsEqualTo(1);
            await Assert.That(branches.Count(item => HasExactType(item, NullType))).IsEqualTo(1);
            return;
        }
        var types = schema.GetProperty(McpSchemaInspector.Type);
        var actual = types.ValueKind == JsonValueKind.Array
            ? types.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(new[] { types.GetString()! }, StringComparer.Ordinal);
        await Assert.That(types.ValueKind == JsonValueKind.Array ? types.GetArrayLength() : 1).IsEqualTo(2);
        await Assert.That(actual.SetEquals(new[] { valueType, NullType })).IsTrue();
    }

    private static JsonElement FindTypeBranch(JsonElement root, JsonElement schema, string type)
    {
        if (!schema.TryGetProperty(McpSchemaInspector.AnyOf, out var variants))
        { return schema; }
        foreach (var variant in variants.EnumerateArray())
        {
            var branch = Resolve(root, variant);
            if (HasExactType(branch, type))
            { return branch; }
        }
        throw new InvalidOperationException(MissingExpectedBranchMessage);
    }

    private static bool HasExactType(JsonElement schema, string type)
    {
        if (!schema.TryGetProperty(McpSchemaInspector.Type, out var actual))
        { return false; }
        return actual.ValueKind == JsonValueKind.String
            ? actual.GetString() == type
            : actual.ValueKind == JsonValueKind.Array && actual.GetArrayLength() == 1 &&
              actual[0].GetString() == type;
    }

    private static async Task VerifyObjectAsync(JsonElement schema, string[] fields, string[] required)
    {
        await Assert.That(HasExactType(schema, ObjectType)).IsTrue();
        await VerifyObjectShapeAsync(schema, fields, required);
    }

    private static async Task VerifyObjectShapeAsync(JsonElement schema, string[] fields, string[] required)
    {
        await Assert.That(schema.GetProperty(PartitionQueryMcpProtocol.AdditionalProperties).ValueKind)
            .IsEqualTo(JsonValueKind.False);
        var actualFields = schema.GetProperty(McpSchemaInspector.Properties).EnumerateObject()
            .Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        await Assert.That(actualFields.SetEquals(fields)).IsTrue();
        var actualRequired = schema.GetProperty(McpSchemaInspector.Required).EnumerateArray()
            .Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
        await Assert.That(actualRequired.SetEquals(required)).IsTrue();
    }

    private static async Task VerifyPrimitiveAsync(JsonElement root, JsonElement schema, string type)
    {
        schema = Resolve(root, schema);
        await Assert.That(HasExactType(schema, type)).IsTrue();
    }

    private static async Task VerifyIntegerAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        if (HasExactType(schema, IntegerType))
        { return; }
        var type = schema.GetProperty(McpSchemaInspector.Type);
        await Assert.That(type.ValueKind).IsEqualTo(JsonValueKind.Array);
        var types = type.EnumerateArray().ToArray();
        await Assert.That(types.Length).IsEqualTo(2);
        await Assert.That(types.All(item => item.ValueKind == JsonValueKind.String)).IsTrue();
        await Assert.That(types.Count(item => item.GetString() == IntegerType)).IsEqualTo(1);
        await Assert.That(types.Count(item => item.GetString() == StringType)).IsEqualTo(1);
    }

    private static JsonElement Resolve(JsonElement root, JsonElement schema)
        => schema.TryGetProperty(McpSchemaInspector.Ref, out var reference)
            ? McpSchemaInspector.Resolve(root, reference.GetString()!) : schema;
}
