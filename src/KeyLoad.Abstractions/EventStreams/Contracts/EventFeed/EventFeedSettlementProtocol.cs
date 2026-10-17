namespace KeyLoad;

/// <summary>Names complete authorized event-feed acknowledgement and release operations.</summary>
public static class EventFeedSettlementProtocol
{
    /// <summary>The unchanged public event-feed protocol version.</summary>
    public const int Version = EventFeedOfferProtocol.Version;
    /// <summary>The complete acknowledgement HTTP route.</summary>
    public const string AcknowledgeRoute = "/v1/event-feeds/acknowledge";
    /// <summary>The official MCP and Q1 acknowledgement identity.</summary>
    public const string AcknowledgeTool = "keyload_acknowledge_event_feed";
    /// <summary>Static acknowledgement guidance without private inventories.</summary>
    public const string AcknowledgeDescription = "Acknowledge the exact original offered vector only after every authorized source advance has settled, retaining the original terminal result and receipt.";
    /// <summary>The complete release HTTP route.</summary>
    public const string ReleaseRoute = "/v1/event-feeds/release";
    /// <summary>The official MCP and Q1 release identity.</summary>
    public const string ReleaseTool = "keyload_release_event_feed";
    /// <summary>Static release guidance without private inventories.</summary>
    public const string ReleaseDescription = "Release an authorized event-feed map after every original source Pin disposition is observed; unsettled ownership remains charged and cannot be resent.";
}
