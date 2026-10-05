using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Independently freezes generated public partition-placement MCP schemas.</summary>
internal static class McpPartitionPlacementSchemaAssertions
{
    private const string BindName = McpCallerProtocol.AdminPartitionPlacementBind;
    private const string ReadName = McpCallerProtocol.AdminPartitionPlacementRead;
    private const int MaximumReferenceDepth = 8;
    private const string UnknownTool = "The MCP placement tool name is not recognized.";
    private const string InvalidReference = "The MCP placement schema reference is not local.";
    private const string ExcessReferenceDepth = "The MCP placement schema reference is too deep.";
    private static readonly ImmutableArray<string> PartitionFields =
    [McpDiscoveryProtocol.TenantId, McpDiscoveryProtocol.DatabaseId,
        McpDiscoveryProtocol.TransactionDomainId, McpDiscoveryProtocol.PartitionKey];
    private static readonly ImmutableArray<string> ResolutionFields =
    [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.PhysicalShardId,
        McpDiscoveryProtocol.Incarnation, McpDiscoveryProtocol.VoterIds, McpDiscoveryProtocol.PlacementEpoch,
        McpDiscoveryProtocol.DirectoryRevision, McpDiscoveryProtocol.Revision, McpDiscoveryProtocol.IsFallback];

    internal static async Task VerifyAsync(string name, JsonElement input, JsonElement output)
    {
        if (name == BindName)
        {
            await VerifyBindAsync(input, output);
            return;
        }
        if (name == ReadName)
        {
            await VerifyReadAsync(input, output);
            return;
        }
        throw new InvalidOperationException(UnknownTool);
    }

    private static async Task VerifyBindAsync(JsonElement input, JsonElement output)
    {
        var request = Resolve(input, input.GetProperty(McpDiscoveryProtocol.Definitions)
            .GetProperty(McpCallerProtocol.Request));
        var fields = ImmutableArray.Create(McpDiscoveryProtocol.Version, McpDiscoveryProtocol.ExpectedRevision,
            McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.PhysicalShardId);
        await VerifyObjectAsync(request, fields, fields);
        var properties = request.GetProperty(McpDiscoveryProtocol.Properties);
        await VerifyPrimitiveAsync(input, properties.GetProperty(McpDiscoveryProtocol.Version), McpDiscoveryProtocol.Integer);
        await VerifyPrimitiveAsync(input, properties.GetProperty(McpDiscoveryProtocol.ExpectedRevision), McpDiscoveryProtocol.Integer);
        await VerifyPartitionAsync(input, properties.GetProperty(McpDiscoveryProtocol.Partition));
        await VerifyPrimitiveAsync(input, properties.GetProperty(McpDiscoveryProtocol.PhysicalShardId), McpDiscoveryProtocol.String);
        var result = Resolve(output, output.GetProperty(McpDiscoveryProtocol.Definitions).GetProperty(McpCallerProtocol.Result));
        await Assert.That(HasType(result, McpDiscoveryProtocol.Boolean)).IsTrue();
    }

    private static async Task VerifyReadAsync(JsonElement input, JsonElement output)
    {
        var request = Resolve(input, input.GetProperty(McpDiscoveryProtocol.Definitions)
            .GetProperty(McpCallerProtocol.Request));
        var requestFields = ImmutableArray.Create(McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Partition);
        await VerifyObjectAsync(request, requestFields, requestFields);
        var requestProperties = request.GetProperty(McpDiscoveryProtocol.Properties);
        await VerifyPrimitiveAsync(input, requestProperties.GetProperty(McpDiscoveryProtocol.Version), McpDiscoveryProtocol.Integer);
        await VerifyPartitionAsync(input, requestProperties.GetProperty(McpDiscoveryProtocol.Partition));

        var result = Resolve(output, output.GetProperty(McpDiscoveryProtocol.Definitions).GetProperty(McpCallerProtocol.Result));
        await VerifyObjectAsync(result, ResolutionFields, ResolutionFields);
        var properties = result.GetProperty(McpDiscoveryProtocol.Properties);
        await VerifyPrimitiveAsync(output, properties.GetProperty(McpDiscoveryProtocol.Version), McpDiscoveryProtocol.Integer);
        await VerifyPartitionAsync(output, properties.GetProperty(McpDiscoveryProtocol.Partition));
        await VerifyPrimitiveAsync(output, properties.GetProperty(McpDiscoveryProtocol.PhysicalShardId), McpDiscoveryProtocol.String);
        await VerifyPrimitiveAsync(output, properties.GetProperty(McpDiscoveryProtocol.Incarnation), McpDiscoveryProtocol.String);
        await VerifyPrimitiveAsync(output, properties.GetProperty(McpDiscoveryProtocol.PlacementEpoch), McpDiscoveryProtocol.Integer);
        await VerifyPrimitiveAsync(output, properties.GetProperty(McpDiscoveryProtocol.DirectoryRevision), McpDiscoveryProtocol.Integer);
        await VerifyPrimitiveAsync(output, properties.GetProperty(McpDiscoveryProtocol.Revision), McpDiscoveryProtocol.Integer);
        await VerifyPrimitiveAsync(output, properties.GetProperty(McpDiscoveryProtocol.IsFallback), McpDiscoveryProtocol.Boolean);
        var voters = Resolve(output, properties.GetProperty(McpDiscoveryProtocol.VoterIds));
        await Assert.That(voters.GetProperty(McpDiscoveryProtocol.Type).GetString()).IsEqualTo(McpDiscoveryProtocol.Array);
        await VerifyPrimitiveAsync(output, voters.GetProperty(McpDiscoveryProtocol.Items), McpDiscoveryProtocol.String);
    }

    private static async Task VerifyPartitionAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        await VerifyObjectAsync(schema, PartitionFields, PartitionFields);
        foreach (var field in PartitionFields)
        { await VerifyPrimitiveAsync(root, schema.GetProperty(McpDiscoveryProtocol.Properties).GetProperty(field), McpDiscoveryProtocol.String); }
    }

    private static async Task VerifyObjectAsync(JsonElement schema, ImmutableArray<string> fields,
        ImmutableArray<string> required)
    {
        await Assert.That(schema.GetProperty(McpDiscoveryProtocol.Type).GetString()).IsEqualTo(McpDiscoveryProtocol.Object);
        await Assert.That(schema.GetProperty(McpDiscoveryProtocol.AdditionalProperties).GetBoolean()).IsFalse();
        var actual = schema.GetProperty(McpDiscoveryProtocol.Properties).EnumerateObject()
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        await Assert.That(actual.SetEquals(fields)).IsTrue();
        var actualRequired = schema.GetProperty(McpDiscoveryProtocol.Required).EnumerateArray()
            .Select(field => field.GetString()!).ToHashSet(StringComparer.Ordinal);
        await Assert.That(actualRequired.SetEquals(required)).IsTrue();
    }

    private static async Task VerifyPrimitiveAsync(JsonElement root, JsonElement schema, string expected)
    {
        schema = Resolve(root, schema);
        await Assert.That(HasType(schema, expected)).IsTrue();
    }

    private static bool HasType(JsonElement schema, string expected)
    {
        if (schema.TryGetProperty(McpDiscoveryProtocol.Type, out var type))
        {
            if (type.ValueKind == JsonValueKind.String && type.GetString() == expected)
            { return true; }
            if (type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(value => value.GetString() == expected))
            { return true; }
        }
        foreach (var keyword in new[] { McpDiscoveryProtocol.AnyOf, McpDiscoveryProtocol.OneOf })
        {
            if (schema.TryGetProperty(keyword, out var variants) && variants.EnumerateArray().Any(value => HasType(value, expected)))
            { return true; }
        }
        return false;
    }

    private static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        for (var depth = 0; depth < MaximumReferenceDepth
            && schema.TryGetProperty(McpDiscoveryProtocol.ReferenceKeyword, out var reference); depth++)
        {
            var pointer = reference.GetString();
            if (pointer is null || !pointer.StartsWith("#/", StringComparison.Ordinal))
            { throw new InvalidOperationException(InvalidReference); }
            schema = root;
            foreach (var segment in pointer[2..].Split('/'))
            {
                var name = segment.Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);
                schema = schema.GetProperty(name);
            }
        }
        if (schema.TryGetProperty(McpDiscoveryProtocol.ReferenceKeyword, out _))
        { throw new InvalidOperationException(ExcessReferenceDepth); }
        return schema;
    }
}
