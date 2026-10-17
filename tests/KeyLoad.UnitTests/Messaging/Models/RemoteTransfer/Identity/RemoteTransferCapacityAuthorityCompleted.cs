namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record RemoteTransferCapacityAuthorityCompleted(RemoteTransferCapacityAuthorityTransfer Transfer,
    CommandRequest Accept, OperationResult Accepted, QueueTransferReceiptInspection Proof,
    CommandRequest Complete, OperationResult Completed, ReceiveRequest Receive, OperationResult Received,
    DeliveryCommand Acknowledge, OperationResult Acknowledged, long ReadySequence);
