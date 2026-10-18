namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record SubscriptionFilterClusterUpdated(SubscriptionFilterClusterOriginal Original,
    ConfigureSubscriptionRequest Request, SubscriptionInfo Result);
