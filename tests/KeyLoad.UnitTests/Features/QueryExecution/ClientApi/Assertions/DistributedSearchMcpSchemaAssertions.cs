using System.Text.Json;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class DistributedSearchMcpSchemaAssertions
{
    internal static async Task RequireAsync()
    {
        if (!McpOperationCatalog.TryGetTool(DistributedSearchMcpProtocol.Tool, out var descriptor))
        { throw new InvalidOperationException(DistributedSearchMcpProtocol.Missing); }
        await Assert.That(descriptor!.Route).IsEqualTo(DistributedSearchMcpProtocol.Route);
        await Assert.That(descriptor.ReadKind).IsEqualTo(GrainReadKind.DistributedSearch);
        await Assert.That(descriptor.CommandKind).IsNull();
        var tool = descriptor.CreateTool();
        await Assert.That(tool.Annotations!.ReadOnlyHint).IsTrue();
        await Assert.That(tool.Annotations.IdempotentHint).IsTrue();
        await Assert.That(tool.Annotations.DestructiveHint).IsFalse();
        await InputAsync(tool.InputSchema);
        await OutputAsync(tool.OutputSchema!.Value);
        await DistributedSearchMcpDecodeAssertions.RequireAsync(descriptor);
        await DistributedSearchMcpPayloadAssertions.RequireAsync();
    }

    private static async Task InputAsync(JsonElement root)
    {
        await ObjectAsync(root, [McpCanonicalTestData.RequestKey], [McpCanonicalTestData.RequestKey]);
        var request = Shape(root, McpSchemaInspector.DefinedRequest(root));
        await ObjectAsync(request, DistributedSearchMcpProtocol.RequestFields, DistributedSearchMcpProtocol.RequestFields);
        var fields = request.GetProperty(McpSchemaInspector.Properties);
        await PrimitiveAsync(root, fields.GetProperty(DistributedSearchMcpProtocol.Version), McpSchemaInspector.Integer);
        var partitions = Shape(root, fields.GetProperty(DistributedSearchMcpProtocol.Partitions));
        await Assert.That(McpSchemaInspector.HasType(partitions, McpSchemaInspector.Array)).IsTrue();
        await PartitionAsync(root, partitions.GetProperty(McpSchemaInspector.Items));
        var search = Shape(root, fields.GetProperty(DistributedSearchMcpProtocol.Search));
        await ObjectAsync(search, DistributedSearchMcpProtocol.SearchFields,
            [DistributedSearchMcpProtocol.Partition, DistributedSearchMcpProtocol.Collection]);
        var searchFields = search.GetProperty(McpSchemaInspector.Properties);
        await PartitionAsync(root, searchFields.GetProperty(DistributedSearchMcpProtocol.Partition));
        await PrimitiveAsync(root, searchFields.GetProperty(DistributedSearchMcpProtocol.Collection), McpSchemaInspector.String);
        foreach (var field in DistributedSearchMcpProtocol.NullableStrings)
        { await PrimitiveAsync(root, searchFields.GetProperty(field), McpSchemaInspector.String); }
        foreach (var field in DistributedSearchMcpProtocol.Weights)
        { await PrimitiveAsync(root, searchFields.GetProperty(field), DistributedSearchMcpProtocol.Number); }
        await PrimitiveAsync(root, searchFields.GetProperty(DistributedSearchMcpProtocol.Fusion), McpSchemaInspector.Integer);
        await PrimitiveAsync(root, searchFields.GetProperty(DistributedSearchMcpProtocol.Explain), DistributedSearchMcpProtocol.Boolean);
        await ArrayAsync(root, searchFields.GetProperty(DistributedSearchMcpProtocol.Vector), DistributedSearchMcpProtocol.Number);
        await ArrayAsync(root, searchFields.GetProperty(DistributedSearchMcpProtocol.AllowedIds), McpSchemaInspector.String);
        await ObjectAsync(Shape(root, searchFields.GetProperty(DistributedSearchMcpProtocol.Space)),
            DistributedSearchMcpProtocol.SpaceFields, DistributedSearchMcpProtocol.SpaceFields);
        var limit = Shape(root, searchFields.GetProperty(DistributedSearchMcpProtocol.Limit));
        await PrimitiveAsync(root, limit, McpSchemaInspector.Integer);
        await Assert.That(limit.GetProperty(McpSchemaInspector.Default).GetInt32()).IsEqualTo(DistributedSearchMcpProtocol.DefaultLimit);
    }

    private static async Task OutputAsync(JsonElement root)
    {
        var page = Shape(root, root.GetProperty(McpSchemaInspector.Defs).GetProperty(McpSchemaInspector.Result));
        await ObjectAsync(page, DistributedSearchMcpProtocol.ResultFields, DistributedSearchMcpProtocol.ResultFields);
        var fields = page.GetProperty(McpSchemaInspector.Properties);
        await PrimitiveAsync(root, fields.GetProperty(DistributedSearchMcpProtocol.Version), McpSchemaInspector.Integer);
        await PrimitiveAsync(root, fields.GetProperty(DistributedSearchMcpProtocol.Epoch), McpSchemaInspector.String);
        await PrimitiveAsync(root, fields.GetProperty(DistributedSearchMcpProtocol.Complete), DistributedSearchMcpProtocol.Boolean);
        var hits = Shape(root, fields.GetProperty(DistributedSearchMcpProtocol.Hits));
        await Assert.That(McpSchemaInspector.HasType(hits, McpSchemaInspector.Array)).IsTrue();
        var hit = Shape(root, hits.GetProperty(McpSchemaInspector.Items));
        await ObjectAsync(hit, [DistributedSearchMcpProtocol.Document, DistributedSearchMcpProtocol.Score, DistributedSearchMcpProtocol.Explanation],
            [DistributedSearchMcpProtocol.Document, DistributedSearchMcpProtocol.Score]);
        await PrimitiveAsync(root, hit.GetProperty(McpSchemaInspector.Properties).GetProperty(DistributedSearchMcpProtocol.Score), DistributedSearchMcpProtocol.Number);
        await DocumentAsync(root, hit.GetProperty(McpSchemaInspector.Properties).GetProperty(DistributedSearchMcpProtocol.Document));
        await ExplanationAsync(root, hit.GetProperty(McpSchemaInspector.Properties).GetProperty(DistributedSearchMcpProtocol.Explanation));
        var leaves = Shape(root, fields.GetProperty(DistributedSearchMcpProtocol.Leaves));
        await Assert.That(McpSchemaInspector.HasType(leaves, McpSchemaInspector.Array)).IsTrue();
        var leaf = Shape(root, leaves.GetProperty(McpSchemaInspector.Items));
        string[] leafFields = [DistributedSearchMcpProtocol.Partition, DistributedSearchMcpProtocol.CutPosition, DistributedSearchMcpProtocol.Policy, DistributedSearchMcpProtocol.Schema, DistributedSearchMcpProtocol.Access];
        await ObjectAsync(leaf, leafFields, leafFields);
        var leafProperties = leaf.GetProperty(McpSchemaInspector.Properties);
        await PartitionAsync(root, leafProperties.GetProperty(DistributedSearchMcpProtocol.Partition));
        await PrimitiveAsync(root, leafProperties.GetProperty(DistributedSearchMcpProtocol.CutPosition), McpSchemaInspector.Integer);
        await PrimitiveAsync(root, leafProperties.GetProperty(DistributedSearchMcpProtocol.Policy), McpSchemaInspector.Integer);
        await PrimitiveAsync(root, leafProperties.GetProperty(DistributedSearchMcpProtocol.Schema), McpSchemaInspector.Integer);
        await PrimitiveAsync(root, leafProperties.GetProperty(DistributedSearchMcpProtocol.Access), McpSchemaInspector.String);
    }

    private static async Task DocumentAsync(JsonElement root, JsonElement schema)
    {
        var document = Shape(root, schema);
        string[] fields = [DistributedSearchMcpProtocol.Reference, DistributedSearchMcpProtocol.RevisionField, DistributedSearchMcpProtocol.Json, DistributedSearchMcpProtocol.Redacted, DistributedSearchMcpProtocol.RedactedFields];
        await ObjectAsync(document, fields, fields);
        var documentProperties = document.GetProperty(McpSchemaInspector.Properties);
        await PrimitiveAsync(root, documentProperties.GetProperty(DistributedSearchMcpProtocol.RevisionField), McpSchemaInspector.Integer);
        await PrimitiveAsync(root, documentProperties.GetProperty(DistributedSearchMcpProtocol.Json), McpSchemaInspector.String);
        await PrimitiveAsync(root, documentProperties.GetProperty(DistributedSearchMcpProtocol.Redacted), DistributedSearchMcpProtocol.Boolean);
        await ArrayAsync(root, documentProperties.GetProperty(DistributedSearchMcpProtocol.RedactedFields), McpSchemaInspector.String);
        var reference = Shape(root, document.GetProperty(McpSchemaInspector.Properties).GetProperty(DistributedSearchMcpProtocol.Reference));
        string[] referenceFields = [DistributedSearchMcpProtocol.Partition, DistributedSearchMcpProtocol.Collection, DistributedSearchMcpProtocol.Id];
        await ObjectAsync(reference, referenceFields, referenceFields);
        var referenceProperties = reference.GetProperty(McpSchemaInspector.Properties);
        await PrimitiveAsync(root, referenceProperties.GetProperty(DistributedSearchMcpProtocol.Collection), McpSchemaInspector.String);
        await PrimitiveAsync(root, referenceProperties.GetProperty(DistributedSearchMcpProtocol.Id), McpSchemaInspector.String);
        await PartitionAsync(root, reference.GetProperty(McpSchemaInspector.Properties).GetProperty(DistributedSearchMcpProtocol.Partition));
    }

    private static async Task ExplanationAsync(JsonElement root, JsonElement schema)
    {
        await Assert.That(McpSchemaInspector.HasType(schema, McpSchemaInspector.Null)).IsTrue();
        var explanation = Shape(root, schema);
        await ObjectAsync(explanation, [DistributedSearchMcpProtocol.Fusion, DistributedSearchMcpProtocol.Contributions],
            [DistributedSearchMcpProtocol.Fusion, DistributedSearchMcpProtocol.Contributions]);
        var contributions = Shape(root, explanation.GetProperty(McpSchemaInspector.Properties).GetProperty(DistributedSearchMcpProtocol.Contributions));
        await Assert.That(McpSchemaInspector.HasType(contributions, McpSchemaInspector.Array)).IsTrue();
        string[] fields = [DistributedSearchMcpProtocol.Branch, DistributedSearchMcpProtocol.Rank, DistributedSearchMcpProtocol.Weight, DistributedSearchMcpProtocol.Contribution];
        var contribution = Shape(root, contributions.GetProperty(McpSchemaInspector.Items));
        await ObjectAsync(contribution, fields, fields);
        var values = contribution.GetProperty(McpSchemaInspector.Properties);
        await PrimitiveAsync(root, values.GetProperty(DistributedSearchMcpProtocol.Rank), McpSchemaInspector.Integer);
        await PrimitiveAsync(root, values.GetProperty(DistributedSearchMcpProtocol.Weight), DistributedSearchMcpProtocol.Number);
        await PrimitiveAsync(root, values.GetProperty(DistributedSearchMcpProtocol.Contribution), DistributedSearchMcpProtocol.Number);
    }

    private static async Task PartitionAsync(JsonElement root, JsonElement schema)
        => await ObjectAsync(Shape(root, schema), [.. DistributedSearchMcpProtocol.PartitionFields, DistributedSearchMcpProtocol.Atomic],
            DistributedSearchMcpProtocol.PartitionFields);

    private static async Task ObjectAsync(JsonElement schema, string[] fields, string[] required)
    {
        await Assert.That(McpSchemaInspector.HasType(schema, DistributedSearchMcpProtocol.Object)).IsTrue();
        await Assert.That(schema.GetProperty(DistributedSearchMcpProtocol.Closed).GetBoolean()).IsFalse();
        await Assert.That(schema.GetProperty(McpSchemaInspector.Properties).EnumerateObject().Select(item => item.Name)
            .ToHashSet(StringComparer.Ordinal).SetEquals(fields)).IsTrue();
        await Assert.That(schema.GetProperty(McpSchemaInspector.Required).EnumerateArray().Select(item => item.GetString()!)
            .ToHashSet(StringComparer.Ordinal).SetEquals(required)).IsTrue();
    }

    private static async Task ArrayAsync(JsonElement root, JsonElement schema, string itemType)
    {
        var array = Shape(root, schema);
        await Assert.That(McpSchemaInspector.HasType(array, McpSchemaInspector.Array)).IsTrue();
        await PrimitiveAsync(root, array.GetProperty(McpSchemaInspector.Items), itemType);
    }

    private static async Task PrimitiveAsync(JsonElement root, JsonElement schema, string type)
        => await Assert.That(McpSchemaInspector.HasType(Shape(root, schema), type)).IsTrue();

    private static JsonElement Shape(JsonElement root, JsonElement schema)
    {
        if (schema.TryGetProperty(McpSchemaInspector.Ref, out var reference))
        { return Shape(root, McpSchemaInspector.Resolve(root, reference.GetString()!)); }
        if (schema.TryGetProperty(McpSchemaInspector.AnyOf, out var branches))
        { return Shape(root, branches.EnumerateArray().First(item => !McpSchemaInspector.HasType(item, McpSchemaInspector.Null))); }
        return schema;
    }
}
