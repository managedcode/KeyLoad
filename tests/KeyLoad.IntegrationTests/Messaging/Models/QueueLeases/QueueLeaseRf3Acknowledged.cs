namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueLeaseRf3Acknowledged(QueueLeaseRf3Original Original,
    DeliveryCommand Ack, CommitReceipt AckReceipt, MessageInspection Acked,
    CommandRequest Healthy, CommitReceipt HealthyReceipt);
