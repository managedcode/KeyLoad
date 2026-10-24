namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record QueueProducerClusterImage(DocumentResult Document,
    MessageInspection Ready, MessageInspection Scheduled, AtomicProducerClusterEvents Events);
