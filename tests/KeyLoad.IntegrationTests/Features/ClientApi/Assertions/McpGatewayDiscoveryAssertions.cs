using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Checks compact public discovery independently of the server's internal catalog.</summary>
internal static class McpGatewayDiscoveryAssertions
{
    private const string MaximumCategories = "maxCategories";
    private const string MaximumToolsPerCategory = "maxToolsPerCategory";
    private const string PreferReadOnly = "preferReadOnly";
    private static readonly string[] Names =
        [McpCallerProtocol.GatewaySearch, McpCallerProtocol.GatewayRoute, McpCallerProtocol.GatewayInvoke];

    /// <summary>Requires exactly three native tools with strict input schemas and conservative effect hints.</summary>
    internal static async Task VerifyAsync(IEnumerable<Tool> tools)
    {
        var actual = tools.ToArray();
        await Assert.That(actual.Length).IsEqualTo(McpCallerProtocol.InitialToolCount);
        await Assert.That(actual.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal).SetEquals(Names)).IsTrue();
        foreach (var tool in actual)
        {
            await VerifyToolAsync(tool);
        }
    }

    private static async Task VerifyToolAsync(Tool tool)
    {
        var invoke = tool.Name == McpCallerProtocol.GatewayInvoke;
        string[] fields = tool.Name switch
        {
            McpCallerProtocol.GatewaySearch => [McpDiscoveryProtocol.Query, McpCallerProtocol.MaximumResults],
            McpCallerProtocol.GatewayRoute => [McpDiscoveryProtocol.Query, MaximumCategories, MaximumToolsPerCategory, PreferReadOnly],
            McpCallerProtocol.GatewayInvoke => [McpCallerProtocol.ToolId, McpCallerProtocol.Arguments],
            _ => throw new InvalidOperationException(McpCallerProtocol.MissingDiscoveredTool)
        };
        var input = tool.InputSchema;
        await Assert.That(input.GetProperty(McpDiscoveryProtocol.Type).GetString()).IsEqualTo(McpDiscoveryProtocol.Object);
        await Assert.That(input.GetProperty(McpDiscoveryProtocol.AdditionalProperties).GetBoolean()).IsFalse();
        await Assert.That(input.GetProperty(McpDiscoveryProtocol.Properties).EnumerateObject()
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(fields)).IsTrue();
        var required = input.GetProperty(McpDiscoveryProtocol.Required).EnumerateArray().Select(item => item.GetString()).ToArray();
        await Assert.That(required.Length).IsEqualTo(1);
        await Assert.That(required[0]).IsEqualTo(invoke ? McpCallerProtocol.ToolId : McpDiscoveryProtocol.Query);
        await Assert.That(string.IsNullOrWhiteSpace(tool.Description)).IsFalse();
        await Assert.That(tool.OutputSchema.HasValue).IsTrue();
        await Assert.That(tool.Annotations).IsNotNull();
        await Assert.That(tool.Annotations!.ReadOnlyHint).IsEqualTo(!invoke);
        await Assert.That(tool.Annotations.IdempotentHint).IsEqualTo(!invoke);
        await Assert.That(tool.Annotations.DestructiveHint).IsEqualTo(invoke);
    }
}
