namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record SubscriptionConcurrentRequestPublicState(ReceiveSubscriptionRequest First,
    ReceiveSubscriptionResult FirstResult, ReceiveSubscriptionRequest Remaining,
    ReceiveSubscriptionResult RemainingResult, ReceiveSubscriptionRequest Empty,
    ReceiveSubscriptionResult EmptyResult, EventSourcePage Source)
{
    internal List<(SubscriptionDeliveryCommand Request, CommitReceipt Receipt)> History { get; } = [];
}
