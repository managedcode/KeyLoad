namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record InboxProcessingRf3State(QueueLaneRef Lane, MessagingRf3Identity Identity,
    ProcessingRequest Original, CommitReceipt Receipt, MessageInspection Input, MessageInspection Output,
    DocumentResult Document);
