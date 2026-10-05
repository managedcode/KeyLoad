using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphIncomingMcpSchemaAssertions
{
    private const int MaximumReferenceDepth = 8;
    private const string InvalidReferenceMessage = "The incoming graph schema reference is not local.";
    private const string ExcessReferenceMessage = "The incoming graph schema reference is too deep.";
    private static readonly ImmutableArray<string> RequestFields =
        [GraphIncomingMcpProtocol.Version, GraphIncomingMcpProtocol.Target, GraphIncomingMcpProtocol.Graph, GraphIncomingMcpProtocol.Limit];
    private static readonly ImmutableArray<string> PageFields =
        [GraphIncomingMcpProtocol.Version, GraphIncomingMcpProtocol.Rows, GraphIncomingMcpProtocol.CutPosition, GraphIncomingMcpProtocol.Projection];
    private static readonly ImmutableArray<string> RowFields =
        [GraphIncomingMcpProtocol.Edge, GraphIncomingMcpProtocol.DeliveredRevision];
    private static readonly ImmutableArray<string> EdgeFields =
        [GraphIncomingMcpProtocol.EntityId, GraphIncomingMcpProtocol.From, GraphIncomingMcpProtocol.To,
            GraphIncomingMcpProtocol.Label, GraphIncomingMcpProtocol.AttributesJson, GraphIncomingMcpProtocol.Revision];
    private static readonly ImmutableArray<string> EntityFields =
        [GraphIncomingMcpProtocol.Partition, GraphIncomingMcpProtocol.Collection, GraphIncomingMcpProtocol.EntityId];
    private static readonly ImmutableArray<string> PartitionFields =
        [GraphIncomingMcpProtocol.TenantId, GraphIncomingMcpProtocol.DatabaseId,
            GraphIncomingMcpProtocol.TransactionDomainId, GraphIncomingMcpProtocol.PartitionKey];

    internal static async Task VerifyAsync(JsonElement input, JsonElement output)
    {
        await VerifyObjectAsync(input, [GraphIncomingMcpProtocol.Request]);
        var request = Resolve(input, input.GetProperty(GraphIncomingMcpProtocol.Definitions)
            .GetProperty(GraphIncomingMcpProtocol.Request));
        await VerifyObjectAsync(request, RequestFields);
        var requestProperties = request.GetProperty(GraphIncomingMcpProtocol.Properties);
        await VerifyPrimitiveAsync(input, requestProperties.GetProperty(GraphIncomingMcpProtocol.Version), GraphIncomingMcpProtocol.Integer);
        await VerifyPrimitiveAsync(input, requestProperties.GetProperty(GraphIncomingMcpProtocol.Graph), GraphIncomingMcpProtocol.String);
        await VerifyPrimitiveAsync(input, requestProperties.GetProperty(GraphIncomingMcpProtocol.Limit), GraphIncomingMcpProtocol.Integer);
        await VerifyEntityAsync(input, requestProperties.GetProperty(GraphIncomingMcpProtocol.Target));
        await VerifyPageAsync(output);
    }

    private static async Task VerifyPageAsync(JsonElement output)
    {
        var page = Resolve(output, output.GetProperty(GraphIncomingMcpProtocol.Definitions)
            .GetProperty(GraphIncomingMcpProtocol.Result));
        await VerifyObjectAsync(page, PageFields);
        var properties = page.GetProperty(GraphIncomingMcpProtocol.Properties);
        await VerifyPrimitiveAsync(output, properties.GetProperty(GraphIncomingMcpProtocol.Version), GraphIncomingMcpProtocol.Integer);
        await VerifyPrimitiveAsync(output, properties.GetProperty(GraphIncomingMcpProtocol.CutPosition), GraphIncomingMcpProtocol.Integer);
        await VerifyPrimitiveAsync(output, properties.GetProperty(GraphIncomingMcpProtocol.Projection), GraphIncomingMcpProtocol.String);
        await VerifyRowsAsync(output, properties.GetProperty(GraphIncomingMcpProtocol.Rows));
    }

    private static async Task VerifyRowsAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await Assert.That(schema.GetProperty(GraphIncomingMcpProtocol.Type).GetString()).IsEqualTo(GraphIncomingMcpProtocol.Array);
        var row = Resolve(root, schema.GetProperty(GraphIncomingMcpProtocol.Items));
        await VerifyObjectAsync(row, RowFields);
        var properties = row.GetProperty(GraphIncomingMcpProtocol.Properties);
        await VerifyEdgeAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.Edge));
        await VerifyPrimitiveAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.DeliveredRevision), GraphIncomingMcpProtocol.Integer);
    }

    private static async Task VerifyEdgeAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyObjectAsync(schema, EdgeFields);
        var properties = schema.GetProperty(GraphIncomingMcpProtocol.Properties);
        foreach (var field in new[] { GraphIncomingMcpProtocol.EntityId, GraphIncomingMcpProtocol.Label, GraphIncomingMcpProtocol.AttributesJson })
        { await VerifyPrimitiveAsync(root, properties.GetProperty(field), GraphIncomingMcpProtocol.String); }
        await VerifyPrimitiveAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.Revision), GraphIncomingMcpProtocol.Integer);
        await VerifyEntityAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.From));
        await VerifyEntityAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.To));
    }

    private static async Task VerifyEntityAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyObjectAsync(schema, EntityFields);
        var properties = schema.GetProperty(GraphIncomingMcpProtocol.Properties);
        await VerifyPrimitiveAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.Collection), GraphIncomingMcpProtocol.String);
        await VerifyPrimitiveAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.EntityId), GraphIncomingMcpProtocol.String);
        await VerifyPartitionAsync(root, properties.GetProperty(GraphIncomingMcpProtocol.Partition));
    }

    private static async Task VerifyPartitionAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyObjectAsync(schema, PartitionFields.Add(GraphIncomingMcpProtocol.AtomicPartitionId), PartitionFields);
        foreach (var field in PartitionFields)
        { await VerifyPrimitiveAsync(root, schema.GetProperty(GraphIncomingMcpProtocol.Properties).GetProperty(field), GraphIncomingMcpProtocol.String); }
    }

    private static async Task VerifyPrimitiveAsync(JsonElement root, JsonElement schema, string expected)
    {
        schema = Resolve(root, schema);
        await Assert.That(schema.GetProperty(GraphIncomingMcpProtocol.Type).GetString()).IsEqualTo(expected);
    }

    private static Task VerifyObjectAsync(JsonElement schema, ImmutableArray<string> fields)
        => VerifyObjectAsync(schema, fields, fields);

    private static async Task VerifyObjectAsync(JsonElement schema, ImmutableArray<string> fields,
        ImmutableArray<string> requiredFields)
    {
        await Assert.That(schema.GetProperty(GraphIncomingMcpProtocol.Type).GetString()).IsEqualTo(GraphIncomingMcpProtocol.Object);
        await Assert.That(schema.GetProperty(GraphIncomingMcpProtocol.AdditionalProperties).GetBoolean()).IsFalse();
        var properties = schema.GetProperty(GraphIncomingMcpProtocol.Properties).EnumerateObject()
            .Select(item => item.Name).ToImmutableHashSet(StringComparer.Ordinal);
        var required = schema.GetProperty(GraphIncomingMcpProtocol.Required).EnumerateArray()
            .Select(item => item.GetString()!).ToImmutableHashSet(StringComparer.Ordinal);
        await Assert.That(properties.SetEquals(fields)).IsTrue();
        await Assert.That(required.SetEquals(requiredFields)).IsTrue();
    }

    private static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        for (var depth = 0; depth < MaximumReferenceDepth
            && schema.TryGetProperty(GraphIncomingMcpProtocol.Reference, out var reference); depth++)
        {
            var pointer = reference.GetString();
            if (pointer is null || !pointer.StartsWith("#/", StringComparison.Ordinal))
            { throw new InvalidOperationException(InvalidReferenceMessage); }
            schema = root;
            foreach (var segment in pointer[2..].Split('/'))
            { schema = schema.GetProperty(segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)); }
        }
        if (schema.TryGetProperty(GraphIncomingMcpProtocol.Reference, out _))
        { throw new InvalidOperationException(ExcessReferenceMessage); }
        return schema;
    }
}
