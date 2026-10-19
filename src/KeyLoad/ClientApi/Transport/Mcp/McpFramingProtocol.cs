namespace KeyLoad.Server;

/// <summary>Bounds private MCP framing without changing the canonical database protocol.</summary>
internal static class McpFramingProtocol
{
    internal const string Path = "/mcp";
    internal const string InvalidFrame = "The MCP frame has invalid UTF-8 or JSON framing.";
    internal const string FrameBudgetExceeded = "The MCP frame exceeds its byte or structural budget.";
    internal const string CanonicalPayloadExceeded = "The canonical JSON payload exceeds its byte budget.";
    internal const string Identifier = "id";
    internal const int MaximumDepth = 64;
    internal const int MaximumReplyDepth = 61;
    internal const int MaximumIdentifierBytes = 256;
    internal const int MaximumTokens = 131_072;
    internal const int MaximumProperties = 32_768;
    internal const int MaximumPropertyNameBytes = 256;
    internal const int MaximumDataReplyBytes = 16_777_216;
    internal const int MaximumControlReplyBytes = 65_536;
    internal const int EnvelopeAllowanceBytes = 65_536;
}
