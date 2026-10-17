namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record SubscriptionFilterRf3Original(SubscriptionFilterRf3Seed Seed,
    ReceiveSubscriptionRequest FirstRequest, ReceiveSubscriptionResult First,
    ReceiveSubscriptionRequest SecondRequest, ReceiveSubscriptionResult Second,
    SubscriptionDeliveryCommand AckFirst, CommitReceipt FirstReceipt,
    SubscriptionDeliveryCommand AckThird, CommitReceipt ThirdReceipt, EventSourcePage History);
