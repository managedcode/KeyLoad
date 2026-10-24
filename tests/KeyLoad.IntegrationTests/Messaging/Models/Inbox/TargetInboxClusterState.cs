namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record TargetInboxClusterState(QueueLaneRef Source, QueueLaneRef Target, MessagingClusterIdentity Identity,
    Delivery Delivery, CommitInboxRequest Original, CommitInboxResult Result, DeliveryCommand SourceAck, CommitReceipt SourceAckReceipt,
    MessageInspection Input, MessageInspection Output, DocumentResult Document);
