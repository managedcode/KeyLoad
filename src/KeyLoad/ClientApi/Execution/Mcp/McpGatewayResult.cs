using ManagedCode.MCPGateway;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Extracts only the original result returned by a successful local AIFunction invocation.</summary>
internal static class McpGatewayResult
{
    internal static global::ManagedCode.Communication.Result<CallToolResult?> GetOriginal(McpGatewayInvokeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? ((CallToolResult?)(result.McpResult)) is { } outcomeValue1 ? global::ManagedCode.Communication.Result<CallToolResult?>.Succeed(outcomeValue1) : global::ManagedCode.Communication.Result<CallToolResult?>.FailNotFound(nameof(GetOriginal)) : global::ManagedCode.Communication.Result<CallToolResult?>.FailNotFound(nameof(GetOriginal));
    }
}
