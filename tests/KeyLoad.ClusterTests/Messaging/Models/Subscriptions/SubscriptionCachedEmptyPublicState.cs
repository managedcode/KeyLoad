namespace KeyLoad.ClusterTests.Messaging;

internal sealed record SubscriptionCachedEmptyPublicState(ConfigureSubscriptionRequest Configure, SubscriptionInfo Configured,
    ReceiveSubscriptionRequest Empty, ReceiveSubscriptionResult Original, CommandRequest Publish, CommitReceipt Published,
    EventSourcePage Source)
{
    internal SubscriptionDeliveryCommand? Ack
    {
        get; set;
    }
    internal CommitReceipt? Acknowledged
    {
        get; set;
    }
}
