using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Exercises graph discovery and invocation through the genuine official MCP SDK.</summary>
internal static class McpGatewayCaller
{
    private const int ExactNameSearchLimit = 1;

    /// <summary>Invokes a canonical operation through the public gateway meta tool.</summary>
    internal static ValueTask<CallToolResult> InvokeKeyLoadToolAsync(this McpClient client,
        string toolName, IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default)
        => client.CallToolAsync(McpCallerProtocol.GatewayInvoke, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [McpCallerProtocol.ToolId] = toolName,
            [McpCallerProtocol.Arguments] = arguments ?? new Dictionary<string, object?>(StringComparer.Ordinal)
        }, cancellationToken: cancellationToken);

    /// <summary>Searches the actual native graph and requires the exact selected operation metadata.</summary>
    internal static async Task<Tool> DiscoverKeyLoadToolAsync(this McpClient client, string toolName,
        CancellationToken cancellationToken)
    {
        var reply = await client.CallToolAsync(McpCallerProtocol.GatewaySearch,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [McpDiscoveryProtocol.Query] = toolName,
                [McpCallerProtocol.MaximumResults] = ExactNameSearchLimit
            }, cancellationToken: cancellationToken);
        await Assert.That(reply.IsError is true).IsFalse();
        var envelope = reply.StructuredContent
            ?? throw new InvalidOperationException(McpCallerProtocol.MissingStructuredResult);
        await Assert.That(envelope.GetProperty(McpCallerProtocol.RequestId).ValueKind).IsEqualTo(JsonValueKind.Null);
        var matches = envelope.GetProperty(McpCallerProtocol.Result).GetProperty(McpCallerProtocol.Matches);
        await Assert.That(matches.GetArrayLength()).IsLessThanOrEqualTo(ExactNameSearchLimit);
        var tools = matches.EnumerateArray().Select(match =>
            match.GetProperty(McpCallerProtocol.Tool).Deserialize<Tool>(McpJsonUtilities.DefaultOptions)
                ?? throw new InvalidOperationException(McpCallerProtocol.MissingDiscoveredTool)).ToArray();
        await Assert.That(tools.Select(tool => tool.Name).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(tools.Length);
        var selected = tools.Where(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal)).ToArray();
        await Assert.That(selected.Length).IsEqualTo(1).Because(McpCallerProtocol.MissingDiscoveredTool);
        return selected.Single();
    }
}
