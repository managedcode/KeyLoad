namespace KeyLoad;

/// <summary>The complete authorized event-feed coverage refresh protocol.</summary>
public static class EventFeedRefreshProtocol
{
    /// <summary>The unchanged event-feed protocol version.</summary>
    public const int Version = EventFeedOfferProtocol.Version;
    /// <summary>The complete Refresh operation HTTP route.</summary>
    public const string Route = "/v1/event-feeds/refresh";
    /// <summary>The official MCP and Q1 Refresh operation identity.</summary>
    public const string Tool = "keyload_refresh_event_feed";
    /// <summary>Static Refresh guidance without private inventories.</summary>
    public const string Description = "Refresh complete authorized source coverage only after every original source Pin, Advance and Release disposition settles, preserving acknowledged positions and the original terminal result.";
}
