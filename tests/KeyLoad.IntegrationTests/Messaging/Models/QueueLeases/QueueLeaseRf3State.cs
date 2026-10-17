namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueLeaseRf3Seed(QueueLaneRef Lane, QueueLaneRef FutureLane, DateTimeOffset FutureDue, MessageInspection FutureSnapshot, ResourceDefinition Resource,
    MessagingRf3Identity Worker, MessagingRf3Identity Foreign,
    CommandRequest Enqueue, CommitReceipt EnqueueReceipt);

internal sealed record QueueLeaseRf3Original(QueueLeaseRf3Seed Seed,
    ReceiveRequest Claim, ReceiveResult Claimed, Delivery Delivery,
    DeliveryCommand Renew, CommitReceipt RenewReceipt,
    DeliveryCommand Nack, CommitReceipt NackReceipt, MessageInspection Scheduled);

internal sealed record QueueLeaseRf3Acknowledged(QueueLeaseRf3Original Original,
    DeliveryCommand Ack, CommitReceipt AckReceipt, MessageInspection Acked,
    CommandRequest Healthy, CommitReceipt HealthyReceipt);
