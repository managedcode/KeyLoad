namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueLeaseClusterSeed(QueueLaneRef Lane, QueueLaneRef FutureLane, DateTimeOffset FutureDue, MessageInspection FutureSnapshot, ResourceDefinition Resource,
    MessagingClusterIdentity Worker, MessagingClusterIdentity Foreign,
    CommandRequest Enqueue, CommitReceipt EnqueueReceipt);
