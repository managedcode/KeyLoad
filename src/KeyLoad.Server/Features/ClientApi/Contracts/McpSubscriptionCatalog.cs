using System.Collections.Immutable;

namespace KeyLoad.Server;

/// <summary>Explicit typed bindings for the frozen subscription commands.</summary>
internal static class McpSubscriptionCatalog
{
    internal static ImmutableArray<McpOperationDescriptor> Entries { get; } =
    [
        McpOperationFactory.Command<ConfigureSubscriptionRequest, SubscriptionInfo>(McpToolNames.SubscriptionsConfigure, McpToolRoutes.SubscriptionsConfigure, OperationKind.ConfigureSubscription, static request => request.CommandId),
        McpOperationFactory.Command<SeekSubscriptionRequest, SubscriptionInfo>(McpToolNames.SubscriptionsSeek, McpToolRoutes.SubscriptionsSeek, OperationKind.SeekSubscription, static request => request.CommandId),
        McpOperationFactory.Command<ReceiveSubscriptionRequest, ReceiveSubscriptionResult>(McpToolNames.SubscriptionsReceive, McpToolRoutes.SubscriptionsReceive, OperationKind.ReceiveSubscription, static request => request.RequestId),
        McpOperationFactory.Command<SubscriptionDeliveryCommand, CommitReceipt>(McpToolNames.SubscriptionsComplete, McpToolRoutes.SubscriptionsComplete, OperationKind.SubscriptionDelivery, static request => request.CommandId),
        McpOperationFactory.Command<SubscriptionProcessingRequest, SubscriptionProcessingResult>(McpToolNames.SubscriptionsProcess, McpToolRoutes.SubscriptionsProcess, OperationKind.SubscriptionProcessing, static request => request.CommandId),
        McpOperationFactory.Command<SetSubscriptionPausedRequest, SubscriptionInfo>(McpToolNames.SubscriptionsPause, McpToolRoutes.SubscriptionsPause, OperationKind.SetSubscriptionPaused, static request => request.CommandId)
    ];
}
