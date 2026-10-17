using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

/// <summary>Explicit canonical native MCP policy for real framing, admission and authentication tests.</summary>
internal static class UnitMcpOptions
{
    internal static IOptions<McpExecutionOptions> Execution(McpExecutionOptions? configured = null)
    {
        var value = configured ?? new McpExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }
    internal static IOptions<McpMemoryLimits> Memory(McpMemoryLimits? configured = null)
    {
        var value = configured ?? new McpMemoryLimits();
        value.Validate();
        return Options.Create(value);
    }
}
