namespace KeyLoad.Server;

/// <summary>Owned wire-boundary names for the configured official stateless protocol revision.</summary>
internal static class McpTransportProtocol
{
    internal const string EncodedHeaderPrefix = "=?base64?";
    internal const string EncodedHeaderSuffix = "?=";
    internal const string Revision = "2026-07-28";
    internal const string RevisionHeader = "MCP-Protocol-Version";
    internal const string MethodHeader = "Mcp-Method";
    internal const string NameHeader = "Mcp-Name";
    internal const string SessionHeader = "Mcp-Session-Id";
    internal const string LastEventHeader = "Last-Event-ID";
    internal const string Method = "method";
    internal const string Parameters = "params";
    internal const string Name = "name";
    internal const string Uri = "uri";
    internal const string RevisionField = "protocolVersion";
    internal const string Meta = "_meta";
    internal const string InvalidTransport = "The MCP transport headers or metadata are invalid.";
    internal const int MaximumMethodCharacters = 64;
    internal const int MaximumNameCharacters = 256;
}
