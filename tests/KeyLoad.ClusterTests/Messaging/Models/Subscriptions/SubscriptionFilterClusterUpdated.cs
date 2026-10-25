namespace KeyLoad.ClusterTests.Messaging;

internal sealed record SubscriptionFilterClusterUpdated(SubscriptionFilterClusterOriginal Original,
    ConfigureSubscriptionRequest Request, SubscriptionInfo Result);
