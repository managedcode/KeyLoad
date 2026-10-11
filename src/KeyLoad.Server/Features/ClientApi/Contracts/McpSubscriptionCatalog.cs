using System.Collections.Immutable;
using KeyLoad.Server.Features.EventStreams;

namespace KeyLoad.Server;

/// <summary>Explicit typed bindings for the frozen subscription commands.</summary>
internal static class McpSubscriptionCatalog
{
    internal static ImmutableArray<McpOperationDescriptor> Entries { get; } =
    [
        EventFeedTailMcpDescriptor.Create(),
        McpOperationFactory.Command<EventFeedControlRequest, EventFeedControlResult>(EventFeedOpenProtocol.Tool, EventFeedOpenProtocol.Route, OperationKind.EventFeedControl, EventFeedParentArguments.Open),
        McpOperationFactory.Command<EventFeedControlRequest, EventFeedControlResult>(EventFeedOfferProtocol.Tool, EventFeedOfferProtocol.Route, OperationKind.EventFeedControl, EventFeedParentArguments.Offer),
        McpOperationFactory.Command<EventFeedControlRequest, EventFeedControlResult>(EventFeedRefreshProtocol.Tool, EventFeedRefreshProtocol.Route, OperationKind.EventFeedControl, EventFeedParentArguments.Refresh),
        McpOperationFactory.Command<EventFeedControlRequest, EventFeedControlResult>(EventFeedSettlementProtocol.AcknowledgeTool, EventFeedSettlementProtocol.AcknowledgeRoute, OperationKind.EventFeedControl, EventFeedParentArguments.Acknowledge),
        McpOperationFactory.Command<EventFeedControlRequest, EventFeedControlResult>(EventFeedSettlementProtocol.ReleaseTool, EventFeedSettlementProtocol.ReleaseRoute, OperationKind.EventFeedControl, EventFeedParentArguments.Release),
        McpOperationFactory.Command<ConfigureSubscriptionRequest, SubscriptionInfo>(McpToolNames.SubscriptionsConfigure, McpToolRoutes.SubscriptionsConfigure, OperationKind.ConfigureSubscription, static request => request.CommandId),
        McpOperationFactory.Command<SeekSubscriptionRequest, SubscriptionInfo>(McpToolNames.SubscriptionsSeek, McpToolRoutes.SubscriptionsSeek, OperationKind.SeekSubscription, static request => request.CommandId),
        McpOperationFactory.Command<ReceiveSubscriptionRequest, ReceiveSubscriptionResult>(McpToolNames.SubscriptionsReceive, McpToolRoutes.SubscriptionsReceive, OperationKind.ReceiveSubscription, static request => request.RequestId),
        McpOperationFactory.Command<SubscriptionDeliveryCommand, CommitReceipt>(McpToolNames.SubscriptionsComplete, McpToolRoutes.SubscriptionsComplete, OperationKind.SubscriptionDelivery, static request => request.CommandId),
        McpOperationFactory.Command<SubscriptionProcessingRequest, SubscriptionProcessingResult>(McpToolNames.SubscriptionsProcess, McpToolRoutes.SubscriptionsProcess, OperationKind.SubscriptionProcessing, static request => request.CommandId),
        McpOperationFactory.Command<SetSubscriptionPausedRequest, SubscriptionInfo>(McpToolNames.SubscriptionsPause, McpToolRoutes.SubscriptionsPause, OperationKind.SetSubscriptionPaused, static request => request.CommandId)
    ];
}
