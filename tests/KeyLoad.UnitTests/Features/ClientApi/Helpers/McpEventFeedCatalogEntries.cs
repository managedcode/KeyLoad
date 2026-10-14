using System.Collections.Immutable;
using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class McpEventFeedCatalogEntries
{
    private const string TailTool = "keyload_tail_event_feed";
    private const string TailRoute = "/v1/event-feeds/tail";
    private const string OpenTool = "keyload_open_event_feed";
    private const string OpenRoute = "/v1/event-feeds/open";
    private const string OfferTool = "keyload_offer_event_feed";
    private const string OfferRoute = "/v1/event-feeds/offer";
    private const string RefreshTool = "keyload_refresh_event_feed";
    private const string RefreshRoute = "/v1/event-feeds/refresh";
    private const string AcknowledgeTool = "keyload_acknowledge_event_feed";
    private const string AcknowledgeRoute = "/v1/event-feeds/acknowledge";
    private const string ReleaseTool = "keyload_release_event_feed";
    private const string ReleaseRoute = "/v1/event-feeds/release";

    internal static ImmutableArray<(string Name, string Route, GrainReadKind? ReadKind, OperationKind? CommandKind)> All
    {
        get;
    } =
    [
        (TailTool, TailRoute, null, OperationKind.EventFeedControl),
        (OpenTool, OpenRoute, null, OperationKind.EventFeedControl),
        (OfferTool, OfferRoute, null, OperationKind.EventFeedControl),
        (RefreshTool, RefreshRoute, null, OperationKind.EventFeedControl),
        (AcknowledgeTool, AcknowledgeRoute, null, OperationKind.EventFeedControl),
        (ReleaseTool, ReleaseRoute, null, OperationKind.EventFeedControl)
    ];
}
