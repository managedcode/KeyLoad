namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record SubscriptionFilterRf3Updated(SubscriptionFilterRf3Original Original,
    ConfigureSubscriptionRequest Request, SubscriptionInfo Result);
