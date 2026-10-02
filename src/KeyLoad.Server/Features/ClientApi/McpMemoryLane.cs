namespace KeyLoad.Server;

/// <summary>Identifies independent retained-memory pools for MCP request processing.</summary>
internal enum McpMemoryLane
{
    /// <summary>Reserves memory for admitted data operations and their native responses.</summary>
    Data,

    /// <summary>Preserves memory for admitted control operations independently of data saturation.</summary>
    Control,

    /// <summary>Bounds unclassified framing and native protocol parsing before admission handoff.</summary>
    Ingress
}
