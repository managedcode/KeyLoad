namespace KeyLoad;

/// <summary>Canonical public transport identities for independent lane receive composition.</summary>
public static class MultiLaneReceiveProtocol
{
    /// <summary>Safe uncertainty detail when a submitted composition cannot be returned.</summary>
    public const string Interrupted = "The multi-lane receive outcome is incomplete. Retry the original stable lane request IDs.";
    /// <summary>Canonical authenticated HTTP receive composition endpoint.</summary>
    public const string Route = "/v1/queues/receive-across-lanes";
    /// <summary>On-demand operation identity used by MCP discovery and SQL CALL.</summary>
    public const string ToolName = "keyload_messages_receive_across_lanes";
}
