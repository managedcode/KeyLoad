using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-001 independently freezes both public GraphPath tool bodies, outputs and hints.</summary>
internal sealed class McpGraphPathCatalogTests
{
    private const string Version = "version";
    private const string Partition = "partition";
    private const string Graph = "graph";
    private const string From = "from";
    private const string To = "to";
    private const string Query = "query";
    private const string Type = "type";
    private const string Properties = "properties";
    private const string Required = "required";
    private const string Integer = "integer";
    private const string String = "string";
    private const string Object = "object";
    private const string Array = "array";
    private const string Boolean = "boolean";
    private const string Result = "result";
    private const string AdditionalProperties = "additionalProperties";

    [Test]
    public async Task AcMcp001GraphPathCatalogContainsOneEntryPerToolWithReadHints()
    {
        var direct = Find(McpCatalogExpectations.GraphShortestPath);
        var sql = Find(McpCatalogExpectations.QueryGraphPath);
        foreach (var descriptor in new[] { direct, sql })
        {
            var tool = descriptor.CreateTool();
            await Assert.That(descriptor.ReadKind.HasValue).IsTrue();
            await Assert.That(descriptor.CommandKind).IsNull();
            await Assert.That(tool.Annotations!.ReadOnlyHint).IsTrue();
            await Assert.That(tool.Annotations.IdempotentHint).IsTrue();
            await Assert.That(tool.Annotations.DestructiveHint).IsFalse();
        }
        await Assert.That(direct.Route).IsEqualTo(McpGraphPathCatalogProtocol.GraphRoute);
        await Assert.That(sql.Route).IsEqualTo(McpGraphPathCatalogProtocol.SqlRoute);
        await Assert.That(McpCatalogExpectations.Entries.Count(entry =>
            entry.Name == McpCatalogExpectations.GraphShortestPath)).IsEqualTo(1);
        await Assert.That(McpCatalogExpectations.Entries.Count(entry =>
            entry.Name == McpCatalogExpectations.QueryGraphPath)).IsEqualTo(1);
    }

    [Test]
    public async Task AcMcp001DirectPathSchemaHasTheCompleteTypedRequestAndResult()
    {
        var descriptor = Find(McpCatalogExpectations.GraphShortestPath);
        var request = McpSchemaInspector.DefinedRequest(descriptor.InputSchema);
        await VerifyObjectAsync(request,
            [Version, Partition, Graph, From, To, McpGraphPathCatalogProtocol.MaxDepth,
                McpGraphPathCatalogProtocol.MaxVertices, McpGraphPathCatalogProtocol.MaxEdges,
                McpGraphPathCatalogProtocol.Labels],
            [Version, Partition, Graph, From, To]);
        var properties = request.GetProperty(Properties);
        await VerifyTypeAsync(descriptor.InputSchema, properties.GetProperty(Version), Integer);
        await VerifyTypeAsync(descriptor.InputSchema, properties.GetProperty(Graph), String);
        await VerifyTypeAsync(descriptor.InputSchema, properties.GetProperty(Partition), Object);
        await VerifyTypeAsync(descriptor.InputSchema, properties.GetProperty(From), Object);
        await VerifyTypeAsync(descriptor.InputSchema, properties.GetProperty(To), Object);
        await VerifyEntityAsync(descriptor.InputSchema, properties.GetProperty(From));
        await VerifyEntityAsync(descriptor.InputSchema, properties.GetProperty(To));
        await VerifyPartitionAsync(descriptor.InputSchema, properties.GetProperty(Partition));
        await VerifyTypeAsync(descriptor.InputSchema, properties.GetProperty(McpGraphPathCatalogProtocol.MaxDepth), Integer);
        await VerifyTypeAsync(descriptor.InputSchema, properties.GetProperty(McpGraphPathCatalogProtocol.MaxVertices), Integer);
        await VerifyTypeAsync(descriptor.InputSchema, properties.GetProperty(McpGraphPathCatalogProtocol.MaxEdges), Integer);
        var labels = properties.GetProperty(McpGraphPathCatalogProtocol.Labels);
        await Assert.That(McpSchemaInspector.HasType(labels, Array)).IsTrue();
        await Assert.That(McpSchemaInspector.HasType(labels, McpSchemaInspector.Null)).IsTrue();
        await VerifyPathResultAsync(descriptor.OutputSchema);
    }

    [Test]
    public async Task AcMcp001SqlPathSchemaHasVersionAndTypedQueryRequest()
    {
        var descriptor = Find(McpCatalogExpectations.QueryGraphPath);
        var request = McpSchemaInspector.DefinedRequest(descriptor.InputSchema);
        await VerifyObjectAsync(request, [Version, Query], [Version, Query]);
        var properties = request.GetProperty(Properties);
        await VerifyTypeAsync(descriptor.InputSchema, properties.GetProperty(Version), Integer);
        var query = Resolve(descriptor.InputSchema, properties.GetProperty(Query));
        await VerifyObjectAsync(query, [Partition, McpGraphPathCatalogProtocol.Sql, McpGraphPathCatalogProtocol.Parameters, McpGraphPathCatalogProtocol.AllowFullScan, McpGraphPathCatalogProtocol.Cursor], [Partition, McpGraphPathCatalogProtocol.Sql]);
        await VerifyTypeAsync(descriptor.InputSchema, query.GetProperty(Properties).GetProperty(Partition), Object);
        await VerifyPartitionAsync(descriptor.InputSchema, query.GetProperty(Properties).GetProperty(Partition));
        await VerifyTypeAsync(descriptor.InputSchema, query.GetProperty(Properties).GetProperty(McpGraphPathCatalogProtocol.Sql), String);
        await VerifyTypeAsync(descriptor.InputSchema, query.GetProperty(Properties).GetProperty(McpGraphPathCatalogProtocol.AllowFullScan), Boolean);
        await VerifyPathResultAsync(descriptor.OutputSchema);
    }

    private static async Task VerifyPathResultAsync(JsonElement schema)
    {
        var branch = schema.GetProperty(McpSchemaInspector.OneOf)[0];
        var result = branch.GetProperty(Properties).GetProperty(Result);
        var resultReference = McpSchemaInspector.References(result).First();
        result = McpSchemaInspector.Resolve(schema, resultReference);
        await VerifyObjectAsync(result, [Version, McpGraphPathCatalogProtocol.Found, McpGraphPathCatalogProtocol.Hops, McpGraphPathCatalogProtocol.Vertices, McpGraphPathCatalogProtocol.Edges, McpGraphPathCatalogProtocol.CutPosition],
            [Version, McpGraphPathCatalogProtocol.Found, McpGraphPathCatalogProtocol.Hops, McpGraphPathCatalogProtocol.Vertices, McpGraphPathCatalogProtocol.Edges, McpGraphPathCatalogProtocol.CutPosition]);
        var properties = result.GetProperty(Properties);
        await VerifyTypeAsync(schema, properties.GetProperty(Version), Integer);
        await VerifyTypeAsync(schema, properties.GetProperty(McpGraphPathCatalogProtocol.Found), Boolean);
        await VerifyTypeAsync(schema, properties.GetProperty(McpGraphPathCatalogProtocol.CutPosition), Integer);
        await VerifyTypeAsync(schema, properties.GetProperty(McpGraphPathCatalogProtocol.Vertices), Array);
        await VerifyTypeAsync(schema, properties.GetProperty(McpGraphPathCatalogProtocol.Edges), Array);
        await Assert.That(McpSchemaInspector.HasType(properties.GetProperty(McpGraphPathCatalogProtocol.Hops), Integer)).IsTrue();
        await Assert.That(McpSchemaInspector.HasType(properties.GetProperty(McpGraphPathCatalogProtocol.Hops), McpSchemaInspector.Null)).IsTrue();
        await VerifyEntityAsync(schema, properties.GetProperty(McpGraphPathCatalogProtocol.Vertices)
            .GetProperty(McpGraphPathCatalogProtocol.Items));
        await VerifyEdgeAsync(schema, properties.GetProperty(McpGraphPathCatalogProtocol.Edges)
            .GetProperty(McpGraphPathCatalogProtocol.Items));
    }

    private static async Task VerifyEntityAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyObjectAsync(schema, [Partition, McpGraphPathCatalogProtocol.Collection, McpGraphPathCatalogProtocol.Id],
            [Partition, McpGraphPathCatalogProtocol.Collection, McpGraphPathCatalogProtocol.Id]);
        var partition = Resolve(root, schema.GetProperty(Properties).GetProperty(Partition));
        await VerifyObjectAsync(partition, [McpGraphPathCatalogProtocol.TenantId, McpGraphPathCatalogProtocol.DatabaseId, McpGraphPathCatalogProtocol.TransactionDomainId, McpGraphPathCatalogProtocol.PartitionKey],
            [McpGraphPathCatalogProtocol.TenantId, McpGraphPathCatalogProtocol.DatabaseId, McpGraphPathCatalogProtocol.TransactionDomainId, McpGraphPathCatalogProtocol.PartitionKey]);
        var properties = schema.GetProperty(Properties);
        await VerifyTypeAsync(root, properties.GetProperty(McpGraphPathCatalogProtocol.Collection), String);
        await VerifyTypeAsync(root, properties.GetProperty(McpGraphPathCatalogProtocol.Id), String);
        foreach (var field in partition.GetProperty(Properties).EnumerateObject())
        { await VerifyTypeAsync(root, field.Value, String); }
    }

    private static async Task VerifyPartitionAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        var fields = new[] { McpGraphPathCatalogProtocol.TenantId, McpGraphPathCatalogProtocol.DatabaseId,
            McpGraphPathCatalogProtocol.TransactionDomainId, McpGraphPathCatalogProtocol.PartitionKey };
        await VerifyObjectAsync(schema, fields, fields);
        foreach (var field in schema.GetProperty(Properties).EnumerateObject())
        { await VerifyTypeAsync(root, field.Value, String); }
    }

    private static async Task VerifyEdgeAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyObjectAsync(schema, [McpGraphPathCatalogProtocol.Id, From, To, McpGraphPathCatalogProtocol.Label, McpGraphPathCatalogProtocol.AttributesJson, McpGraphPathCatalogProtocol.Revision],
            [McpGraphPathCatalogProtocol.Id, From, To, McpGraphPathCatalogProtocol.Label, McpGraphPathCatalogProtocol.AttributesJson, McpGraphPathCatalogProtocol.Revision]);
        await VerifyEntityAsync(root, schema.GetProperty(Properties).GetProperty(From));
        await VerifyEntityAsync(root, schema.GetProperty(Properties).GetProperty(To));
        var properties = schema.GetProperty(Properties);
        await VerifyTypeAsync(root, properties.GetProperty(McpGraphPathCatalogProtocol.Id), String);
        await VerifyTypeAsync(root, properties.GetProperty(McpGraphPathCatalogProtocol.Label), String);
        await VerifyTypeAsync(root, properties.GetProperty(McpGraphPathCatalogProtocol.AttributesJson), String);
        await VerifyTypeAsync(root, properties.GetProperty(McpGraphPathCatalogProtocol.Revision), Integer);
    }

    private static async Task VerifyObjectAsync(JsonElement schema, string[] properties, string[] required)
    {
        await Assert.That(schema.GetProperty(Type).GetString()).IsEqualTo(Object);
        await Assert.That(schema.GetProperty(AdditionalProperties).GetBoolean()).IsFalse();
        await Assert.That(schema.GetProperty(Properties).EnumerateObject().Select(value => value.Name))
            .IsEquivalentTo(properties);
        await Assert.That(schema.GetProperty(Required).EnumerateArray().Select(value => value.GetString()!))
            .IsEquivalentTo(required);
    }

    private static async Task VerifyTypeAsync(JsonElement root, JsonElement schema, string expected)
    {
        schema = Resolve(root, schema);
        await Assert.That(McpSchemaInspector.HasType(schema, expected)).IsTrue();
    }

    private static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        var reference = McpSchemaInspector.References(schema).FirstOrDefault();
        return reference is null ? schema : McpSchemaInspector.Resolve(root, reference);
    }

    private static McpOperationDescriptor Find(string name)
    {
        if (!McpOperationCatalog.TryGetTool(name, out var descriptor))
        { throw new InvalidOperationException(name); }
        return descriptor!;
    }
}
