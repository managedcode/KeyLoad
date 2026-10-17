namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueProducerRf3Healthy(QueueProducerRf3Original Original,
    CommandRequest Command, CommitReceipt Receipt, DeliveryCommand Ack, CommitReceipt AckReceipt,
    MessageInspection Acked, QueueProducerRf3Image Image);
