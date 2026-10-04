namespace KeyLoad.Server;

/// <summary>Closed MCP method categories safe for internal transport diagnostics.</summary>
internal enum McpTransportMethodCategory
{
    /// <summary>The official SDK server discovery request.</summary>
    Discovery,
    /// <summary>The protocol initialization handshake.</summary>
    Initialize,
    /// <summary>A tool invocation.</summary>
    ToolsCall,
    /// <summary>A tool catalog request.</summary>
    ToolsList,
    /// <summary>A missing or unrecognized method.</summary>
    Other
}
