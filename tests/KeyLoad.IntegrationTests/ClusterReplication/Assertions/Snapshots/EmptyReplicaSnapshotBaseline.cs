namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed record EmptyReplicaSnapshotBaseline(SubscriptionInfo Subscription, OutboxStatus Outbox,
    EventSourcePage Events, ChangeFeedPage Changes);
