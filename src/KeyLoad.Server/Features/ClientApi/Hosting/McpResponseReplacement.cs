using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Replaces bounded native result content while retaining SDK-injected metadata and transport identity.</summary>
internal static class McpResponseReplacement
{
    /// <summary>Moves the actual existing native metadata into a fresh native tool result without cloning it.</summary>
    /// <param name="response">The native response whose context and identifier remain untouched.</param>
    /// <param name="result">The fixed safe result held by its response owner.</param>
    internal static void Apply(JsonRpcResponse response, CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(result);
        var replacement = JsonSerializer.SerializeToNode(result, McpJsonUtilities.DefaultOptions) as JsonObject
            ?? throw new InvalidOperationException(McpCatalogProtocol.InvalidOperation);
        if (response.Result is JsonObject discarded
            && discarded.TryGetPropertyValue(McpTransportProtocol.Meta, out var metadata))
        {
            discarded.Remove(McpTransportProtocol.Meta);
            replacement[McpTransportProtocol.Meta] = metadata;
        }
        response.Result = replacement;
    }
}
