using ManagedCode.MCPGateway;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Extracts only the original result returned by a successful local AIFunction invocation.</summary>
internal static class McpGatewayResult
{
    internal static (bool HasValue, CallToolResult? Value) GetOriginal(McpGatewayInvokeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? ((CallToolResult?)(result.McpResult)) is { } outcomeValue1 ? (true, outcomeValue1) : (false, default(CallToolResult?)) : (false, default(CallToolResult?));
    }
}
