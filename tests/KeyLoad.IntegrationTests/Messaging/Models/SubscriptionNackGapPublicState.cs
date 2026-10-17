namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record SubscriptionNackGapPublicState(ReceiveSubscriptionRequest Request,
    ReceiveSubscriptionResult Claimed, EventSourcePage Source)
{
    internal List<(SubscriptionDeliveryCommand Request, CommitReceipt Receipt)> History { get; } = [];
}
