namespace KeyLoad;

/// <summary>Names the complete authorized Open parent operation.</summary>
public static class EventFeedOpenProtocol
{
    /// <summary>The existing native public protocol version.</summary>
    public const int Version = 1;
    /// <summary>The full Open operation HTTP route.</summary>
    public const string Route = "/v1/event-feeds/open";
    /// <summary>The official MCP and Q1 CALL operation identity.</summary>
    public const string Tool = "keyload_open_event_feed";
    /// <summary>Static public operation guidance without private inventories.</summary>
    public const string Description = "Open an authorized resumable multi-source event feed with retained source pins and a complete native terminal receipt.";
}
