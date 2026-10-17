namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueProducerRf3Image(DocumentResult Document,
    MessageInspection Ready, MessageInspection Scheduled, AtomicProducerRf3Events Events);
