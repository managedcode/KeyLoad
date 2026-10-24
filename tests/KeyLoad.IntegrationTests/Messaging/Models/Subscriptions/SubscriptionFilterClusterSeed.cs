namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record SubscriptionFilterClusterSeed(SubscriptionRef Group, SubscriptionRef Independent,
    MessagingClusterIdentity Manager, MessagingClusterIdentity First, MessagingClusterIdentity Second,
    CommandRequest Publish, CommitReceipt PublishReceipt, ConfigureSubscriptionRequest Replacement);
