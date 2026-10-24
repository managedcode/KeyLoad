namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record InboxProcessingClusterState(QueueLaneRef Lane, MessagingClusterIdentity Identity,
    ProcessingRequest Original, CommitReceipt Receipt, MessageInspection Input, MessageInspection Output,
    DocumentResult Document);
