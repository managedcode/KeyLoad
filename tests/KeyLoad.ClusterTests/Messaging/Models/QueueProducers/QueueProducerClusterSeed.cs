namespace KeyLoad.ClusterTests.Messaging;

internal sealed record QueueProducerClusterSeed(QueueLaneRef Lane, MessagingClusterIdentity Identity,
    CommandRequest Original, DateTimeOffset Due, EntityRefLifetime CollectionLifetime);
