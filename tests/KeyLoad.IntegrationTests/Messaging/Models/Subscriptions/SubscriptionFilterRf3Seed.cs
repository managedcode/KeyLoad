namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record SubscriptionFilterRf3Seed(SubscriptionRef Group, SubscriptionRef Independent,
    MessagingRf3Identity Manager, MessagingRf3Identity First, MessagingRf3Identity Second,
    CommandRequest Publish, CommitReceipt PublishReceipt, ConfigureSubscriptionRequest Replacement);
