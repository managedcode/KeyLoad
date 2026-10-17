namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueLeaseRf3Seed(QueueLaneRef Lane, QueueLaneRef FutureLane, DateTimeOffset FutureDue, MessageInspection FutureSnapshot, ResourceDefinition Resource,
    MessagingRf3Identity Worker, MessagingRf3Identity Foreign,
    CommandRequest Enqueue, CommitReceipt EnqueueReceipt);
