namespace KeyLoad.ClusterTests.Messaging;

internal sealed record QueueDeadlineClusterAcknowledged(DeliveryCommand Command, CommitReceipt Receipt,
    MessageInspection Terminal);
