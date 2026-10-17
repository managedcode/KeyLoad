namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueDeadlineRf3Acknowledged(DeliveryCommand Command, CommitReceipt Receipt,
    MessageInspection Terminal);
