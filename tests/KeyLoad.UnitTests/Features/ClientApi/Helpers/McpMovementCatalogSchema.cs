using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Complete independently frozen movement schemas, including nested scope/owners/nullable receipt and placement.</summary>
internal static class McpMovementCatalogSchema
{
    internal static async Task RequireAsync(McpOperationDescriptor descriptor)
    {
        var input = descriptor.InputSchema;
        await ObjectAsync(input, [McpMovementCatalogProtocol.Request], [McpMovementCatalogProtocol.Request]);
        var request = McpSchemaInspector.DefinedRequest(input);
        await ObjectAsync(request, McpMovementCatalogProtocol.RequestFields, McpMovementCatalogProtocol.RequestFields);
        await McpMovementCatalogScalarSchema.RequireRequestAsync(input, request);
        await PartitionAsync(input, request.GetProperty(McpMovementCatalogProtocol.Properties).GetProperty(McpMovementCatalogProtocol.Partition));
        var output = descriptor.OutputSchema;
        var result = Shape(output, output.GetProperty(McpSchemaInspector.Defs).GetProperty(McpSchemaInspector.Result));
        await ObjectAsync(result, McpMovementCatalogProtocol.ResultFields, McpMovementCatalogProtocol.ResultFields);
        await McpMovementCatalogScalarSchema.RequireResultAsync(output, result);
        var fields = result.GetProperty(McpMovementCatalogProtocol.Properties);
        await PartitionAsync(output, fields.GetProperty(McpMovementCatalogProtocol.Partition));
        await ObjectAsync(Shape(output, fields.GetProperty(McpMovementCatalogProtocol.SourceOwner)), McpMovementCatalogProtocol.OwnerFields, McpMovementCatalogProtocol.OwnerFields);
        await ObjectAsync(Shape(output, fields.GetProperty(McpMovementCatalogProtocol.DestinationOwner)), McpMovementCatalogProtocol.OwnerFields, McpMovementCatalogProtocol.OwnerFields);
        await McpMovementCatalogScalarSchema.RequireOwnerAsync(output, Shape(output, fields.GetProperty(McpMovementCatalogProtocol.SourceOwner)));
        await McpMovementCatalogScalarSchema.RequireOwnerAsync(output, Shape(output, fields.GetProperty(McpMovementCatalogProtocol.DestinationOwner)));
        await NullableAsync(output, fields.GetProperty(McpMovementCatalogProtocol.Receipt), McpMovementCatalogProtocol.ReceiptFields);
        await NullableAsync(output, fields.GetProperty(McpMovementCatalogProtocol.Placement), McpMovementCatalogProtocol.PlacementFields);
    }

    private static async Task NullableAsync(JsonElement root, JsonElement schema, string[] fields)
    {
        await Assert.That(McpSchemaInspector.HasType(schema, McpSchemaInspector.Null)).IsTrue();
        var value = Shape(root, schema);
        await ObjectAsync(value, fields, fields, allowNull: true);
        await McpMovementCatalogScalarSchema.RequireReceiptOrPlacementAsync(root, value, fields);
        if (fields.Contains(McpMovementCatalogProtocol.Partition, StringComparer.Ordinal))
        { await PartitionAsync(root, value.GetProperty(McpMovementCatalogProtocol.Properties).GetProperty(McpMovementCatalogProtocol.Partition)); }
    }

    private static async Task PartitionAsync(JsonElement root, JsonElement schema)
    {
        var value = Shape(root, schema);
        await ObjectAsync(value, [.. McpMovementCatalogProtocol.PartitionFields, McpMovementCatalogProtocol.Atomic], McpMovementCatalogProtocol.PartitionFields);
        await McpMovementCatalogScalarSchema.RequireStringsAsync(root, value, McpMovementCatalogProtocol.PartitionFields);
        var atomic = Shape(root, value.GetProperty(McpMovementCatalogProtocol.Properties).GetProperty(McpMovementCatalogProtocol.Atomic));
        var actualTypes = atomic.GetProperty(McpSchemaInspector.Type).EnumerateArray().Select(type => type.GetString()!).ToArray();
        string[] expectedTypes = [McpSchemaInspector.String, McpSchemaInspector.Null];
        await Assert.That(actualTypes.Length).IsEqualTo(expectedTypes.Length);
        await Assert.That(actualTypes.ToHashSet(StringComparer.Ordinal).SetEquals(expectedTypes)).IsTrue();
    }

    private static async Task ObjectAsync(JsonElement schema, string[] fields, string[] required, bool allowNull = false)
    {
        await Assert.That(McpSchemaInspector.HasType(schema, McpMovementCatalogProtocol.Object)).IsTrue();
        if (!allowNull)
        { await Assert.That(McpSchemaInspector.HasType(schema, McpSchemaInspector.Null)).IsFalse(); }
        await Assert.That(schema.GetProperty(McpMovementCatalogProtocol.Closed).GetBoolean()).IsFalse();
        await Assert.That(schema.GetProperty(McpMovementCatalogProtocol.Properties).EnumerateObject().Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal).SetEquals(fields)).IsTrue();
        await Assert.That(schema.GetProperty(McpMovementCatalogProtocol.Required).EnumerateArray().Select(value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal).SetEquals(required)).IsTrue();
    }

    private static JsonElement Shape(JsonElement root, JsonElement schema)
    {
        if (schema.TryGetProperty(McpSchemaInspector.Ref, out var reference))
        { return Shape(root, McpSchemaInspector.Resolve(root, reference.GetString()!)); }
        if (schema.TryGetProperty(McpSchemaInspector.AnyOf, out var branches))
        { return Shape(root, branches.EnumerateArray().First(value => !McpSchemaInspector.HasType(value, McpSchemaInspector.Null))); }
        return schema;
    }
}
