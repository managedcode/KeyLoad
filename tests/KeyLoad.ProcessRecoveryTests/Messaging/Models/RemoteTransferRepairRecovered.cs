namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal sealed record RemoteTransferRepairRecovered(CommandRequest Complete, CommitReceipt Completed,
    QueueTransferInspection Delivered, QueueTransferReceiptInspection Proof, MessageInspection Message);
