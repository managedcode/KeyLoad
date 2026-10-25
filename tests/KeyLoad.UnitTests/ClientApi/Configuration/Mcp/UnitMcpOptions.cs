using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

/// <summary>Explicit canonical execution MCP policy for real framing and authentication tests.</summary>
internal static class UnitMcpOptions
{
    /// <summary>Gets the MCP canonical payload ceiling that bounds one decoded public request.</summary>
    internal static int CanonicalPayloadBytes => new McpExecutionOptions().MaximumCanonicalPayloadBytes;

    internal static IOptions<McpExecutionOptions> Execution(McpExecutionOptions? configured = null)
    {
        var value = configured ?? new McpExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }
}
