namespace KeyLoad;

/// <summary>Names the complete authorized Offer parent operation.</summary>
public static class EventFeedOfferProtocol
{
    /// <summary>The existing native public protocol version.</summary>
    public const int Version = 1;
    /// <summary>The full Offer operation HTTP route.</summary>
    public const string Route = "/v1/event-feeds/offer";
    /// <summary>The official MCP and Q1 CALL operation identity.</summary>
    public const string Tool = "keyload_offer_event_feed";
    /// <summary>Static public operation guidance without private inventories.</summary>
    public const string Description = "Offer complete bounded source pages from an authorized resumable event feed, retaining the original pages and terminal receipt until explicit acknowledgement.";
}
