namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueProducerRf3Seed(QueueLaneRef Lane, MessagingRf3Identity Identity,
    CommandRequest Original, DateTimeOffset Due);
