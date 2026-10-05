using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class PartitionQueryMcpCatalogAssertions
{
    private const string Route = "/v1/query/partitions";
    private const string Result = "result";
    private const string Reference = "reference";
    private const string Row = "row";
    private const string EntityId = "entityId";
    private const string Json = "json";
    private const string Redacted = "redacted";
    private const string RedactedFields = "redactedFields";
    private const string AccessPath = "accessPath";

    internal static async Task VerifyAsync()
    {
        if (!McpOperationCatalog.TryGetTool(McpCatalogExpectations.QueryPartitions, out var descriptor))
        { throw new InvalidOperationException("The public partition-query tool is missing."); }
        await Assert.That(descriptor!.Route).IsEqualTo(Route);
        await Assert.That(descriptor.ReadKind).IsEqualTo(GrainReadKind.PartitionQuery);
        await Assert.That(descriptor.CommandKind).IsNull();
        var tool = descriptor.CreateTool();
        await Assert.That(tool.Annotations!.ReadOnlyHint).IsTrue();
        await Assert.That(tool.Annotations.IdempotentHint).IsTrue();
        await Assert.That(tool.Annotations.DestructiveHint).IsFalse();
        await VerifyInputAsync(tool.InputSchema);
        await VerifyOutputAsync(tool.OutputSchema!.Value);
    }

    private static async Task VerifyInputAsync(JsonElement schema)
    {
        var body = McpSchemaInspector.DefinedRequest(schema);
        var requestFields = ImmutableArray.Create(PartitionQueryMcpProtocol.Version,
            PartitionQueryMcpProtocol.Partitions, PartitionQueryMcpProtocol.Query,
            PartitionQueryMcpProtocol.Parameters, PartitionQueryMcpProtocol.AllowFullScan,
            PartitionQueryMcpProtocol.AstVersion);
        await VerifyObjectAsync(body, requestFields);
        var properties = body.GetProperty(McpSchemaInspector.Properties);
        await VerifyPrimitiveAsync(schema, properties.GetProperty(PartitionQueryMcpProtocol.Version), McpSchemaInspector.Integer);
        await VerifyPrimitiveAsync(schema, properties.GetProperty(PartitionQueryMcpProtocol.AstVersion), McpSchemaInspector.Integer);
        await VerifyPrimitiveAsync(schema, properties.GetProperty(PartitionQueryMcpProtocol.AllowFullScan), PartitionQueryMcpProtocol.BooleanType);
        await VerifyPartitionArrayAsync(schema, properties.GetProperty(PartitionQueryMcpProtocol.Partitions));
        var query = Resolve(schema, properties.GetProperty(PartitionQueryMcpProtocol.Query));
        await VerifyObjectAsync(query, [PartitionQueryMcpProtocol.Collection, PartitionQueryMcpProtocol.Alias,
                PartitionQueryMcpProtocol.Projection, PartitionQueryMcpProtocol.Filter, PartitionQueryMcpProtocol.Order,
                PartitionQueryMcpProtocol.Limit, PartitionQueryMcpProtocol.Explain, PartitionQueryMcpProtocol.ModelSource],
            [PartitionQueryMcpProtocol.Collection, PartitionQueryMcpProtocol.Alias, PartitionQueryMcpProtocol.Projection,
                PartitionQueryMcpProtocol.Filter, PartitionQueryMcpProtocol.Order, PartitionQueryMcpProtocol.Limit]);
        var parameters = Resolve(schema, properties.GetProperty(PartitionQueryMcpProtocol.Parameters));
        await Assert.That(McpSchemaInspector.HasType(parameters, PartitionQueryMcpProtocol.ObjectType)).IsTrue();
    }

    private static async Task VerifyOutputAsync(JsonElement schema)
    {
        var result = Resolve(schema,
            schema.GetProperty(McpSchemaInspector.Defs).GetProperty(Result));
        var pageFields = ImmutableArray.Create(PartitionQueryMcpProtocol.Version,
            PartitionQueryMcpProtocol.Rows, PartitionQueryMcpProtocol.Leaves, PartitionQueryMcpProtocol.Complete);
        await VerifyObjectAsync(result, pageFields);
        var properties = result.GetProperty(McpSchemaInspector.Properties);
        await VerifyPrimitiveAsync(schema, properties.GetProperty(PartitionQueryMcpProtocol.Version), McpSchemaInspector.Integer);
        await VerifyPrimitiveAsync(schema, properties.GetProperty(PartitionQueryMcpProtocol.Complete), PartitionQueryMcpProtocol.BooleanType);
        await VerifyRowsAsync(schema, properties.GetProperty(PartitionQueryMcpProtocol.Rows));
        await VerifyLeavesAsync(schema, properties.GetProperty(PartitionQueryMcpProtocol.Leaves));
    }

    private static async Task VerifyRowsAsync(JsonElement root, JsonElement schema)
    {
        var item = Resolve(root, schema.GetProperty(McpSchemaInspector.Items));
        await VerifyObjectAsync(item, [Reference, Row]);
        var properties = item.GetProperty(McpSchemaInspector.Properties);
        await VerifyEntityAsync(root, properties.GetProperty(Reference));
        var queryRow = Resolve(root, properties.GetProperty(Row));
        await VerifyObjectAsync(queryRow, [EntityId, PartitionQueryMcpProtocol.Revision, Json, Redacted, RedactedFields],
            [EntityId, PartitionQueryMcpProtocol.Revision, Json]);
        var rowProperties = queryRow.GetProperty(McpSchemaInspector.Properties);
        await VerifyPrimitiveAsync(root, rowProperties.GetProperty(EntityId), McpSchemaInspector.String);
        await VerifyPrimitiveAsync(root, rowProperties.GetProperty(PartitionQueryMcpProtocol.Revision), McpSchemaInspector.Integer);
        await VerifyPrimitiveAsync(root, rowProperties.GetProperty(Json), McpSchemaInspector.String);
        await VerifyPrimitiveAsync(root, rowProperties.GetProperty(Redacted), PartitionQueryMcpProtocol.BooleanType);
        await Assert.That(McpSchemaInspector.HasType(Resolve(root,
            rowProperties.GetProperty(RedactedFields)), McpSchemaInspector.Array)).IsTrue();
    }

    private static async Task VerifyLeavesAsync(JsonElement root, JsonElement schema)
    {
        var item = Resolve(root, schema.GetProperty(McpSchemaInspector.Items));
        var fields = ImmutableArray.Create(PartitionQueryMcpProtocol.Partition,
            PartitionQueryMcpProtocol.CutPosition, PartitionQueryMcpProtocol.PolicyEpoch,
            PartitionQueryMcpProtocol.SchemaVersion, AccessPath);
        await VerifyObjectAsync(item, fields);
        var properties = item.GetProperty(McpSchemaInspector.Properties);
        await VerifyPartitionAsync(root, properties.GetProperty(PartitionQueryMcpProtocol.Partition));
        await VerifyPrimitiveAsync(root, properties.GetProperty(PartitionQueryMcpProtocol.CutPosition), McpSchemaInspector.Integer);
        await VerifyPrimitiveAsync(root, properties.GetProperty(PartitionQueryMcpProtocol.PolicyEpoch), McpSchemaInspector.Integer);
        await VerifyPrimitiveAsync(root, properties.GetProperty(PartitionQueryMcpProtocol.SchemaVersion), McpSchemaInspector.Integer);
        await VerifyPrimitiveAsync(root, properties.GetProperty(AccessPath), McpSchemaInspector.String);
    }

    private static async Task VerifyPartitionArrayAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await Assert.That(McpSchemaInspector.HasType(schema, McpSchemaInspector.Array)).IsTrue();
        var array = schema;
        await VerifyPartitionAsync(root, array.GetProperty(McpSchemaInspector.Items));
    }

    private static async Task VerifyEntityAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyObjectAsync(schema, [PartitionQueryMcpProtocol.Partition, PartitionQueryMcpProtocol.Collection,
            PartitionQueryMcpProtocol.Id]);
        await VerifyPartitionAsync(root, schema.GetProperty(McpSchemaInspector.Properties)
            .GetProperty(PartitionQueryMcpProtocol.Partition));
    }

    private static async Task VerifyPartitionAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        var fields = ImmutableArray.Create(PartitionQueryMcpProtocol.TenantId, PartitionQueryMcpProtocol.DatabaseId,
            PartitionQueryMcpProtocol.TransactionDomainId, PartitionQueryMcpProtocol.PartitionKey);
        await VerifyObjectAsync(schema, [.. fields, PartitionQueryMcpProtocol.AtomicPartitionId], fields);
    }

    private static async Task VerifyObjectAsync(JsonElement schema, ImmutableArray<string> fields,
        ImmutableArray<string> required = default)
    {
        await Assert.That(schema.GetProperty(McpSchemaInspector.Type).GetString()).IsEqualTo(PartitionQueryMcpProtocol.ObjectType);
        await Assert.That(schema.GetProperty(PartitionQueryMcpProtocol.AdditionalProperties).ValueKind)
            .IsEqualTo(JsonValueKind.False);
        var actualFields = schema.GetProperty(McpSchemaInspector.Properties).EnumerateObject()
            .Select(item => item.Name).ToImmutableHashSet(StringComparer.Ordinal);
        await Assert.That(actualFields.SetEquals(fields)).IsTrue();
        if (required.IsDefault) { required = fields; }
        var actualRequired = schema.GetProperty(McpSchemaInspector.Required).EnumerateArray()
            .Select(item => item.GetString()!).ToImmutableHashSet(StringComparer.Ordinal);
        await Assert.That(actualRequired.SetEquals(required)).IsTrue();
    }

    private static async Task VerifyPrimitiveAsync(JsonElement root, JsonElement schema, string type)
    {
        schema = Resolve(root, schema);
        await Assert.That(McpSchemaInspector.HasType(schema, type)).IsTrue();
    }

    private static JsonElement Resolve(JsonElement root, JsonElement schema)
        => schema.TryGetProperty(McpSchemaInspector.Ref, out var reference)
            ? McpSchemaInspector.Resolve(root, reference.GetString()!) : schema;
}
