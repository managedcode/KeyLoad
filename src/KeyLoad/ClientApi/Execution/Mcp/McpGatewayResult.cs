using ManagedCode.MCPGateway;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Extracts only the original result returned by a successful local AIFunction invocation.</summary>
internal static class McpGatewayResult
{
    internal static CallToolResult? GetOriginal(McpGatewayInvokeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? result.McpResult : null;
    }
}
