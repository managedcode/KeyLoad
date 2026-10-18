namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueProducerClusterSeed(QueueLaneRef Lane, MessagingClusterIdentity Identity,
    CommandRequest Original, DateTimeOffset Due);
