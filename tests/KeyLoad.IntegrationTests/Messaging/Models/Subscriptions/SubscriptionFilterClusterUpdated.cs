namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record SubscriptionFilterClusterUpdated(SubscriptionFilterClusterOriginal Original,
    ConfigureSubscriptionRequest Request, SubscriptionInfo Result);
