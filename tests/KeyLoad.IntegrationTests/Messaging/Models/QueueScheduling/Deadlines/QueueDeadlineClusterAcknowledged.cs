namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record QueueDeadlineClusterAcknowledged(DeliveryCommand Command, CommitReceipt Receipt,
    MessageInspection Terminal);
