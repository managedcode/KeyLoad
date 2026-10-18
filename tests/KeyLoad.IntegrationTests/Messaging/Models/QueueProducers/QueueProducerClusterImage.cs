namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueProducerClusterImage(DocumentResult Document,
    MessageInspection Ready, MessageInspection Scheduled, AtomicProducerClusterEvents Events);
