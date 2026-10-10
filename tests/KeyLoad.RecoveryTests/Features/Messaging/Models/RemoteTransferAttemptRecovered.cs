namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed record RemoteTransferAttemptRecovered(CommandRequest Accept, CommitReceipt Accepted,
    CommandRequest Complete, CommitReceipt Completed, QueueTransferInspection Delivered,
    QueueTransferReceiptInspection Proof, MessageInspection Message);
