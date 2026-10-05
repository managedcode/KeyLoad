namespace KeyLoad.Server;

/// <summary>Stable native MCP resource and prompt identity for the static agent guide.</summary>
internal static class McpAgentGuideContract
{
    internal const string ResourceUri = "keyload://guides/agent-quickstart";
    internal const string ResourceName = "KeyLoad agent quickstart";
    internal const string PromptName = "keyload_agent_quickstart";
    internal const string MimeType = "text/markdown";
    internal const string Description = "How to discover and safely use KeyLoad operations.";
    internal const string InvalidCursor = "Resource and prompt pagination is not available.";
    internal const string UnknownResource = "The requested KeyLoad guide is unavailable.";
    internal const string UnknownPrompt = "The requested KeyLoad prompt is unavailable.";
    internal const string PromptArguments = "The KeyLoad quickstart prompt accepts no arguments.";
    internal const int MaximumUtf8Bytes = 8192;
}
