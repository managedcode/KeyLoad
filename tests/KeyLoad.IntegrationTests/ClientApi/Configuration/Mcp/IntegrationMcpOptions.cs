using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests;

/// <summary>Explicit canonical execution MCP policy for real framing and authentication tests.</summary>
[ConfigurationBinding]
internal static class IntegrationMcpOptions
{
    internal static IOptions<McpExecutionOptions> Execution(McpExecutionOptions? configured = null)
    {
        var value = configured ?? new McpExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }
}
