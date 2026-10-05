using System.Collections.Immutable;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Checks actual discovered native schemas against public acceptance without calling internal catalog code.</summary>
internal static class McpDiscoveryAssertions
{
    /// <summary>Requires exactly the public frozen set, with no omitted, duplicate or internal capability.</summary>
    /// <param name="names">The actually discovered names.</param>
    /// <returns>The completed real discovery assertions.</returns>
    internal static async Task VerifyInventoryAsync(IEnumerable<string> names)
    {
        var actual = names.ToArray();
        await Assert.That(actual.Length).IsEqualTo(McpCallerProtocol.ToolCount);
        await Assert.That(actual.ToHashSet(StringComparer.Ordinal).SetEquals(McpCatalogExpectations.Entries.Select(entry => entry.Name))).IsTrue();
    }

    /// <summary>Requires the actual tool's canonical required fields, wrapper schema and explicit effect hints.</summary>
    /// <param name="tool">The real native tool decoded from a real RF3 response.</param>
    /// <returns>The completed observable contract assertions.</returns>
    internal static async Task VerifyAsync(Tool tool)
    {
        var expected = McpCatalogExpectations.Entries.Single(entry => string.Equals(entry.Name, tool.Name, StringComparison.Ordinal));
        await Assert.That(string.IsNullOrWhiteSpace(tool.Description)).IsFalse();
        await VerifyInputAsync(tool.InputSchema, expected);
        await Assert.That(tool.OutputSchema.HasValue).IsTrue();
        await VerifyOutputAsync(tool.OutputSchema!.Value);
        if (tool.Name is McpCallerProtocol.GraphShortestPath or McpCallerProtocol.QueryGraphPath)
        {
            await McpGraphPathSchemaAssertions.VerifyAsync(tool.Name, tool.InputSchema, tool.OutputSchema!.Value,
                expected.ResultFields);
        }
        if (tool.Name is McpCallerProtocol.AdminPartitionPlacementBind or McpCallerProtocol.AdminPartitionPlacementRead)
        {
            await McpPartitionPlacementSchemaAssertions.VerifyAsync(tool.Name, tool.InputSchema, tool.OutputSchema!.Value);
        }
        await Assert.That(tool.Annotations is not null).IsTrue();
        await Assert.That(tool.Annotations!.ReadOnlyHint).IsEqualTo(expected.ReadOnly);
        await Assert.That(tool.Annotations.IdempotentHint).IsEqualTo(expected.Idempotent);
        await Assert.That(tool.Annotations.DestructiveHint).IsEqualTo(expected.Destructive);
    }

    private static async Task VerifyInputAsync(JsonElement schema, McpToolExpectation expected)
    {
        await Assert.That(schema.GetProperty(McpDiscoveryProtocol.Type).GetString()).IsEqualTo(McpDiscoveryProtocol.Object);
        await Assert.That(schema.GetProperty(McpDiscoveryProtocol.AdditionalProperties).GetBoolean()).IsFalse();
        var outer = expected.Body == McpExpectedBody.None ? ImmutableArray<string>.Empty
            : expected.OuterCommandId ? [McpCallerProtocol.Request, McpCallerProtocol.CommandId] : [McpCallerProtocol.Request];
        await RequiredAsync(schema, outer);
        await Assert.That(schema.GetProperty(McpDiscoveryProtocol.Properties).EnumerateObject().Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal).SetEquals(outer)).IsTrue();
        if (expected.Body == McpExpectedBody.None)
        { return; }
        var body = schema.GetProperty(McpDiscoveryProtocol.Definitions).GetProperty(McpCallerProtocol.Request);
        if (expected.Body == McpExpectedBody.Boolean)
        {
            await Assert.That(body.GetProperty(McpDiscoveryProtocol.Type).GetString()).IsEqualTo(McpDiscoveryProtocol.Boolean);
            return;
        }
        await RequiredAsync(body, expected.BodyFields);
        await Assert.That(body.GetProperty(McpDiscoveryProtocol.Properties).EnumerateObject().Any()).IsTrue();
    }

    private static async Task VerifyOutputAsync(JsonElement schema)
    {
        await Assert.That(schema.GetProperty(McpDiscoveryProtocol.Type).GetString()).IsEqualTo(McpDiscoveryProtocol.Object);
        var variants = schema.GetProperty(McpDiscoveryProtocol.OneOf);
        await Assert.That(variants.GetArrayLength()).IsEqualTo(McpDiscoveryProtocol.OutputVariantCount);
        await RequiredAsync(variants[0], [McpCallerProtocol.Result, McpCallerProtocol.RequestId]);
        await RequiredAsync(variants[1], [McpCallerProtocol.Error, McpCallerProtocol.RequestId]);
        await RequiredAsync(schema.GetProperty(McpDiscoveryProtocol.Definitions).GetProperty(McpCallerProtocol.Error),
            McpCallerAssertions.ProblemFields);
    }

    private static async Task RequiredAsync(JsonElement schema, ImmutableArray<string> expected)
    {
        var actual = schema.GetProperty(McpDiscoveryProtocol.Required).EnumerateArray().Select(field => field.GetString()!).ToArray();
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        await Assert.That(actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected)).IsTrue();
    }
}
