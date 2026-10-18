namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueDeadlineClusterAcknowledged(DeliveryCommand Command, CommitReceipt Receipt,
    MessageInspection Terminal);
