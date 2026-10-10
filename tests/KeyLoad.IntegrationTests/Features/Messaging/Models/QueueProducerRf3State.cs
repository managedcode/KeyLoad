namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueProducerRf3Seed(QueueLaneRef Lane, MessagingRf3Identity Identity,
    CommandRequest Original, DateTimeOffset Due);
internal sealed record QueueProducerRf3Image(DocumentResult Document,
    MessageInspection Ready, MessageInspection Scheduled);
internal sealed record QueueProducerRf3Original(QueueProducerRf3Seed Seed, CommitReceipt Receipt,
    QueueProducerRf3Image Image);
internal sealed record QueueProducerRf3Healthy(QueueProducerRf3Original Original,
    CommandRequest Command, CommitReceipt Receipt, DeliveryCommand Ack, CommitReceipt AckReceipt,
    MessageInspection Acked, QueueProducerRf3Image Image);
