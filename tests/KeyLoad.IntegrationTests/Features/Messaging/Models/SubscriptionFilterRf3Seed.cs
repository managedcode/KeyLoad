namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record SubscriptionFilterRf3Seed(SubscriptionRef Group, SubscriptionRef Independent,
    MessagingRf3Identity Manager, MessagingRf3Identity First, MessagingRf3Identity Second,
    CommandRequest Publish, CommitReceipt PublishReceipt, ConfigureSubscriptionRequest Replacement);

internal sealed record SubscriptionFilterRf3Original(SubscriptionFilterRf3Seed Seed,
    ReceiveSubscriptionRequest FirstRequest, ReceiveSubscriptionResult First,
    ReceiveSubscriptionRequest SecondRequest, ReceiveSubscriptionResult Second,
    SubscriptionDeliveryCommand AckFirst, CommitReceipt FirstReceipt,
    SubscriptionDeliveryCommand AckThird, CommitReceipt ThirdReceipt, EventSourcePage History);

internal sealed record SubscriptionFilterRf3Updated(SubscriptionFilterRf3Original Original,
    ConfigureSubscriptionRequest Request, SubscriptionInfo Result);
