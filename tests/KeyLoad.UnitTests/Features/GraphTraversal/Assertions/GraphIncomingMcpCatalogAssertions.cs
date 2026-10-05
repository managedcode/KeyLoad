using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphIncomingMcpCatalogAssertions
{
    private const string MissingTool = "The public graph incoming tool is missing.";

    internal static async Task VerifyAsync()
    {
        if (!McpOperationCatalog.TryGetTool(McpCatalogExpectations.GraphIncomingEdges, out var descriptor))
        { throw new InvalidOperationException(MissingTool); }
        await Assert.That(descriptor!.Route).IsEqualTo(GraphIncomingMcpProtocol.Route);
        await Assert.That(descriptor.ReadKind).IsEqualTo(GrainReadKind.GraphIncomingEdges);
        await Assert.That(descriptor.CommandKind).IsNull();
        await Assert.That(descriptor.ReadOnly).IsTrue();
        await Assert.That(descriptor.Idempotent).IsTrue();
        await Assert.That(descriptor.Destructive).IsFalse();
        await VerifyInputAsync(descriptor.InputSchema);
        await VerifyOutputAsync(descriptor.OutputSchema!);
    }

    private static async Task VerifyInputAsync(JsonElement schema)
    {
        var request = McpSchemaInspector.DefinedRequest(schema);
        var fields = ImmutableArray.Create(GraphIncomingMcpProtocol.Version, GraphIncomingMcpProtocol.Target,
            GraphIncomingMcpProtocol.Graph, GraphIncomingMcpProtocol.Limit);
        await VerifyObjectAsync(request, fields);
        var properties = request.GetProperty(McpSchemaInspector.Properties);
        await VerifyPrimitiveAsync(schema, properties.GetProperty(GraphIncomingMcpProtocol.Version), McpSchemaInspector.Integer);
        await VerifyPrimitiveAsync(schema, properties.GetProperty(GraphIncomingMcpProtocol.Graph), McpSchemaInspector.String);
        await VerifyPrimitiveAsync(schema, properties.GetProperty(GraphIncomingMcpProtocol.Limit), McpSchemaInspector.Integer);
        await VerifyEntityAsync(schema, properties.GetProperty(GraphIncomingMcpProtocol.Target));
    }

    private static async Task VerifyOutputAsync(JsonElement schema)
    {
        var page = Resolve(schema, schema.GetProperty(McpSchemaInspector.Defs).GetProperty(GraphIncomingMcpProtocol.Result));
        var fields = ImmutableArray.Create(GraphIncomingMcpProtocol.Version, GraphIncomingMcpProtocol.Rows,
            GraphIncomingMcpProtocol.CutPosition, GraphIncomingMcpProtocol.Projection);
        await VerifyObjectAsync(page, fields);
        var properties = page.GetProperty(McpSchemaInspector.Properties);
        await VerifyPrimitiveAsync(schema, properties.GetProperty(GraphIncomingMcpProtocol.Version), McpSchemaInspector.Integer);
        await VerifyPrimitiveAsync(schema, properties.GetProperty(GraphIncomingMcpProtocol.CutPosition), McpSchemaInspector.Integer);
        await VerifyPrimitiveAsync(schema, properties.GetProperty(GraphIncomingMcpProtocol.Projection), McpSchemaInspector.String);
        await VerifyRowsAsync(schema, properties.GetProperty(GraphIncomingMcpProtocol.Rows));
    }

    private static async Task VerifyRowsAsync(JsonElement root, JsonElement rows)
    {
        rows = Resolve(root, rows);
        await Assert.That(McpSchemaInspector.HasType(rows, McpSchemaInspector.Array)).IsTrue();
        var row = Resolve(root, rows.GetProperty(McpSchemaInspector.Items));
        var rowFields = ImmutableArray.Create(GraphIncomingMcpProtocol.Edge, GraphIncomingMcpProtocol.DeliveredRevision);
        await VerifyObjectAsync(row, rowFields);
        var properties = row.GetProperty(McpSchemaInspector.Properties);
        await VerifyEdgeAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.Edge));
        await VerifyPrimitiveAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.DeliveredRevision), McpSchemaInspector.Integer);
    }

    private static async Task VerifyEdgeAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        var fields = ImmutableArray.Create(GraphIncomingMcpProtocol.Id, GraphIncomingMcpProtocol.From,
            GraphIncomingMcpProtocol.To, GraphIncomingMcpProtocol.Label, GraphIncomingMcpProtocol.AttributesJson,
            GraphIncomingMcpProtocol.Revision);
        await VerifyObjectAsync(schema, fields);
        var properties = schema.GetProperty(McpSchemaInspector.Properties);
        foreach (var field in new[] { GraphIncomingMcpProtocol.Id, GraphIncomingMcpProtocol.Label,
            GraphIncomingMcpProtocol.AttributesJson })
        { await VerifyPrimitiveAsync(root, properties.GetProperty(field), McpSchemaInspector.String); }
        await VerifyPrimitiveAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.Revision), McpSchemaInspector.Integer);
        await VerifyEntityAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.From));
        await VerifyEntityAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.To));
    }

    private static async Task VerifyEntityAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        var fields = ImmutableArray.Create(GraphIncomingMcpProtocol.Partition, GraphIncomingMcpProtocol.Collection,
            GraphIncomingMcpProtocol.Id);
        await VerifyObjectAsync(schema, fields);
        var properties = schema.GetProperty(McpSchemaInspector.Properties);
        await VerifyPrimitiveAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.Collection), McpSchemaInspector.String);
        await VerifyPrimitiveAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.Id), McpSchemaInspector.String);
        await VerifyPartitionAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.Partition));
    }

    private static async Task VerifyPartitionAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        var fields = ImmutableArray.Create(GraphIncomingMcpProtocol.TenantId, GraphIncomingMcpProtocol.DatabaseId,
            GraphIncomingMcpProtocol.TransactionDomainId, GraphIncomingMcpProtocol.PartitionKey);
        await VerifyObjectAsync(schema, fields.Add(GraphIncomingMcpProtocol.AtomicPartitionId), fields);
        foreach (var field in fields)
        { await VerifyPrimitiveAsync(root, schema.GetProperty(McpSchemaInspector.Properties).GetProperty(field), McpSchemaInspector.String); }
    }

    private static Task VerifyObjectAsync(JsonElement schema, ImmutableArray<string> fields)
        => VerifyObjectAsync(schema, fields, fields);

    private static async Task VerifyObjectAsync(JsonElement schema, ImmutableArray<string> fields,
        ImmutableArray<string> requiredFields)
    {
        await Assert.That(schema.GetProperty(McpSchemaInspector.Type).GetString()).IsEqualTo(GraphIncomingMcpProtocol.Object);
        await Assert.That(schema.GetProperty(GraphIncomingMcpProtocol.AdditionalProperties).GetBoolean()).IsFalse();
        var actual = schema.GetProperty(McpSchemaInspector.Properties).EnumerateObject()
            .Select(property => property.Name).ToImmutableHashSet(StringComparer.Ordinal);
        await Assert.That(actual.SetEquals(fields)).IsTrue();
        var required = schema.GetProperty(McpSchemaInspector.Required).EnumerateArray()
            .Select(item => item.GetString()!).ToImmutableHashSet(StringComparer.Ordinal);
        await Assert.That(required.SetEquals(requiredFields)).IsTrue();
    }

    private static async Task VerifyPrimitiveAsync(JsonElement root, JsonElement schema, string expected)
    {
        schema = Resolve(root, schema);
        await Assert.That(McpSchemaInspector.HasType(schema, expected)).IsTrue();
    }

    private static JsonElement Resolve(JsonElement root, JsonElement schema)
        => schema.TryGetProperty(GraphIncomingMcpProtocol.Ref, out var reference)
            ? McpSchemaInspector.Resolve(root, reference.GetString()!) : schema;
}
