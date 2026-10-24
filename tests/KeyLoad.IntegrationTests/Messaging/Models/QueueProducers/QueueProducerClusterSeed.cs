namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record QueueProducerClusterSeed(QueueLaneRef Lane, MessagingClusterIdentity Identity,
    CommandRequest Original, DateTimeOffset Due, EntityRefLifetime CollectionLifetime);
