namespace KeyLoad.ClusterTests.Messaging;

internal sealed record QueueLeaseClusterAcknowledged(QueueLeaseClusterOriginal Original,
    DeliveryCommand Ack, CommitReceipt AckReceipt, MessageInspection Acked,
    CommandRequest Healthy, CommitReceipt HealthyReceipt);
