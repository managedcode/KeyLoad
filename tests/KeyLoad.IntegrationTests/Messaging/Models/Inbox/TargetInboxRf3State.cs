namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record TargetInboxRf3State(QueueLaneRef Source, QueueLaneRef Target, MessagingRf3Identity Identity,
    Delivery Delivery, CommitInboxRequest Original, CommitInboxResult Result, DeliveryCommand SourceAck, CommitReceipt SourceAckReceipt,
    MessageInspection Input, MessageInspection Output, DocumentResult Document);
